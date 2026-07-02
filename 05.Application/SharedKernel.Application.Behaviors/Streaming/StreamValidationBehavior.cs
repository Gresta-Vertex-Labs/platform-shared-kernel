using FluentValidation;
using MediatR;
using SharedKernel.Primitives.Errors;
using System.Runtime.CompilerServices;
using ValidationException = SharedKernel.Core.Exceptions.ValidationException;

namespace SharedKernel.Application.Behaviors.Streaming;

/// <summary>
/// Validates a streaming request once at stream-open using all registered FluentValidation
/// validators. If validation fails, throws <see cref="ValidationException"/> before any items
/// are yielded — the stream never opens.
/// </summary>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TResponse">The per-item payload type yielded by the stream.</typeparam>
/// <remarks>
/// Mirrors the semantics of <c>ValidationBehavior&lt;,&gt;</c> for the streaming pipeline:
/// validation runs once at entry (before <c>next()</c>), aggregates all failures, and throws
/// <see cref="ValidationException"/> if any are found. When zero validators are registered the
/// stream opens immediately — an empty <see cref="IEnumerable{IValidator}"/> is never null by
/// DI convention and is treated as a no-op.
/// </remarks>
public sealed class StreamValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Validate once at stream-open — same aggregate pattern as ValidationBehavior.
        var validationResults = await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(request, cancellationToken)))
            .ConfigureAwait(false);

        var errors = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => Error.Validation(failure.PropertyName, failure.ErrorMessage))
            .ToList();

        if (errors.Count > 0)
            throw new ValidationException(errors);

        await foreach (var item in next().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
