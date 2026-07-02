using MediatR;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Streaming;
using System.Runtime.CompilerServices;

namespace SharedKernel.Application.Behaviors.Streaming;

/// <summary>
/// Evaluates authorization requirements declared by <see cref="IAuthorizeRequest"/> once at
/// stream-open, before the first item is yielded.
/// </summary>
/// <typeparam name="TRequest">
/// The streaming request type, constrained to <see cref="IAuthorizeRequest"/> and
/// <see cref="IStreamRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The per-item payload type yielded by the stream.</typeparam>
/// <remarks>
/// <para>
/// Authorization is evaluated once per stream-open call — not per yielded item. If the
/// <see cref="IAuthorizationContext"/> check fails, an <see cref="UnauthorizedAccessException"/>
/// is thrown before the inner handler's <see cref="IAsyncEnumerable{T}"/> is opened.
/// </para>
/// <para>
/// Supports the full multi-requirement shape (WO-038, P-232): evaluates
/// <see cref="IAuthorizeRequest.AllOfRequirements"/> (all must pass, short-circuit on first
/// failure), then <see cref="IAuthorizeRequest.AnyOfRequirements"/> (at least one must pass,
/// short-circuit on first success). Both are no-ops on empty collections.
/// </para>
/// <para>
/// <see cref="IStreamQuery{TResponse}"/> types that do <b>not</b> implement
/// <see cref="IAuthorizeRequest"/> never resolve this behavior into their pipeline — a DI-level
/// fact (the constraint on <typeparamref name="TRequest"/> is enforced at registration time), not
/// a runtime branch.
/// </para>
/// <para>
/// <b>Non-applicable streaming behaviors:</b> Transaction, Caching, CacheInvalidation,
/// Idempotency, and Resilience are not applicable to streaming queries. See
/// <c>05.Application/CLAUDE.md</c> for the full rationale.
/// </para>
/// </remarks>
public sealed class StreamAuthorizationBehavior<TRequest, TResponse>(IAuthorizationContext authorizationContext)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuthorizeRequest, IStreamRequest<TResponse>
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Evaluate authorization once at stream-open, before opening the inner enumerator.
        // Step 1: AllOf — all listed requirements must pass (short-circuit on first failure).
        var allOfRequirements = request.AllOfRequirements;
        if (allOfRequirements.Count > 0)
        {
            if (!await authorizationContext.AllOf(allOfRequirements, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException(
                    "The current caller does not satisfy all required AllOf authorization requirements " +
                    $"for streaming request '{typeof(TRequest).FullName ?? typeof(TRequest).Name}'.");
            }
        }
        else if (!string.IsNullOrEmpty(request.Requirement))
        {
            // Backward-compatibility path for the single-string Requirement convenience property.
            if (!await authorizationContext.IsAuthorizedAsync(request.Requirement, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException(
                    $"The current caller is not authorized to satisfy requirement '{request.Requirement}' " +
                    $"for streaming request '{typeof(TRequest).FullName ?? typeof(TRequest).Name}'.");
            }
        }

        // Step 2: AnyOf — at least one listed requirement must pass (short-circuit on first pass).
        var anyOfRequirements = request.AnyOfRequirements;
        if (anyOfRequirements.Count > 0)
        {
            if (!await authorizationContext.AnyOf(anyOfRequirements, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException(
                    "The current caller does not satisfy any of the required AnyOf authorization requirements " +
                    $"for streaming request '{typeof(TRequest).FullName ?? typeof(TRequest).Name}'.");
            }
        }

        // Authorization passed — open the inner stream and yield items.
        await foreach (var item in next().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
