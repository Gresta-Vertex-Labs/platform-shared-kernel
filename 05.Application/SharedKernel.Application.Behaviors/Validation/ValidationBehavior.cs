using FluentValidation;
using MediatR;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Behaviors.Validation;

/// <summary>
/// Runs all registered FluentValidation validators for <typeparamref name="TRequest"/> before the
/// inner pipeline executes.
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Runs every registered validator <b>sequentially</b> — not <c>Task.WhenAll</c> — since a
/// validator may use a scoped resource (e.g. a <c>DbContext</c>) that is not safe for concurrent
/// use. Every failure across every validator is collected and aggregated into a single
/// <c>Error.Validation(errors)</c> via <see cref="Error.Validation(string, string)"/> per failure,
/// grouped by <c>failure.PropertyName</c> — the field-name key <c>14.Presentation</c> uses to build
/// the <c>ProblemDetails</c> <c>errors</c> map. The aggregate is returned as a
/// <c>Result.Failure</c>/<c>Result&lt;T&gt;.Failure</c> — <b>never thrown</b>. When zero validators
/// are registered for <typeparamref name="TRequest"/>, <c>next()</c> is called immediately —
/// <see cref="IEnumerable{IValidator}"/> is empty (never null) by DI convention, so this is a
/// zero-cost no-op, not a missing-registration error. Applies to <b>both</b> commands and queries —
/// <typeparamref name="TRequest"/> is constrained only to <see cref="IRequest{TResponse}"/>.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        List<Error>? errors = null;

        foreach (var validator in validators)
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (validationResult.IsValid)
                continue;

            errors ??= [];
            foreach (var failure in validationResult.Errors)
            {
                if (failure is null)
                    continue;

                errors.Add(Error.Validation(failure.PropertyName, failure.ErrorMessage));
            }
        }

        if (errors is { Count: > 0 })
            return FailureResponse.Create<TResponse>(Error.Validation(errors));

        return await next().ConfigureAwait(false);
    }
}
