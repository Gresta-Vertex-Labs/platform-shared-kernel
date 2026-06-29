using MediatR;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Short-circuits a duplicate command submission using a caller-supplied idempotency key.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IIdempotentRequest"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Constrained to <see cref="ICommandBase"/> only — mirrors <c>TransactionBehavior</c>'s
/// commands-only constraint exactly; never applies to queries (a DI-level fact, since
/// <c>IQuery&lt;TResponse&gt;</c> never implements <see cref="ICommandBase"/>). Calls
/// <see cref="IIdempotencyKeyStore.HasProcessedAsync"/> first. On <see langword="true"/>
/// (duplicate), short-circuits without calling <c>next()</c> a second time and returns
/// <c>Result.Failure(Error.Conflict(...))</c> — this seam intentionally does not cache/replay the
/// original response payload; that is a deliberate scope boundary. On <see langword="false"/>,
/// calls <c>next()</c>, then on success calls
/// <see cref="IIdempotencyKeyStore.MarkProcessedAsync"/> so the key is recorded only after a
/// successful handler run — a faulted handler must be safely retryable with the same key.
/// </remarks>
public sealed class IdempotentCommandBehavior<TRequest, TResponse>(IIdempotencyKeyStore idempotencyKeyStore)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IIdempotentRequest, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var hasProcessed = await idempotencyKeyStore
            .HasProcessedAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (hasProcessed)
        {
            var error = Error.Conflict(
                "idempotency.duplicate",
                $"A request with idempotency key '{request.IdempotencyKey}' has already been processed.");
            return FailureResponseFactory.Create<TResponse>(error);
        }

        var response = await next().ConfigureAwait(false);

        await idempotencyKeyStore
            .MarkProcessedAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }
}
