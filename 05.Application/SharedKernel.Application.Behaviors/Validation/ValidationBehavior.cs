using FluentValidation;
using MediatR;
using SharedKernel.Primitives.Errors;
using ValidationException = SharedKernel.Core.Exceptions.ValidationException;

namespace SharedKernel.Application.Behaviors.Validation;

/// <summary>
/// Runs all registered FluentValidation validators for <typeparamref name="TRequest"/> before the
/// inner pipeline executes.
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Aggregates every failure across every validator into <see cref="IReadOnlyList{Error}"/> via
/// <see cref="Error.Validation"/>, then — if the aggregate is non-empty — throws
/// <see cref="ValidationException"/> without calling <c>next()</c>. This is the platform's
/// existing <c>ValidationResult</c>-to-exception bridge (<c>01.Core</c>), not a new mechanism;
/// the presentation layer's global exception handler converts it into a single multi-error
/// <c>ProblemDetails</c> response. When zero validators are registered for
/// <typeparamref name="TRequest"/>, <c>next()</c> is called immediately —
/// <see cref="IEnumerable{IValidator}"/> is empty (never null) by DI convention, so this is a
/// zero-cost no-op, not a missing-registration error. Applies to <b>both</b> commands and
/// queries — <typeparamref name="TRequest"/> is constrained only to <see cref="IRequest{TResponse}"/>.
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
        if (!validators.Any())
            return await next().ConfigureAwait(false);

        var validationResults = await Task.WhenAll(
                validators.Select(validator => validator.ValidateAsync(request, cancellationToken)))
            .ConfigureAwait(false);

        var errors = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => Error.Validation(failure.PropertyName, failure.ErrorMessage))
            .ToList();

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return await next().ConfigureAwait(false);
    }
}
