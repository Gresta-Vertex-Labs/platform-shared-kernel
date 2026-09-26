using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Validation;

/// <summary>
/// Validates a request before its handler runs.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <remarks>
/// <para>
/// The validation port of the request pipeline. <c>ValidationBehavior</c>
/// (<c>SharedKernel.Application.Pipeline</c>) runs every registered validator for a request, one
/// after another, and returns a single <c>Error.Validation(errors)</c> carrying every error they
/// reported; the handler never runs. No validator library is part of this contract:
/// <c>SharedKernel.Validation.FluentValidation</c>'s <c>AddFluentValidationRequestValidators()</c>
/// bridges FluentValidation's <c>IValidator&lt;T&gt;</c>, and a validator can equally be written by hand.
/// </para>
/// <para>
/// Each returned error should be an <see cref="ErrorType.Validation"/> error with a specific code.
/// Put the failing field's path in <see cref="Error.MessageArguments"/> under
/// <see cref="ErrorArgumentNames.PropertyPath"/> — <c>14.Presentation</c> keys the ProblemDetails
/// <c>errors</c> map by it — and never put the rejected value itself in an error.
/// </para>
/// <para>
/// Validators run sequentially, never concurrently, so a validator may use a scoped resource such as
/// a <c>DbContext</c>.
/// </para>
/// </remarks>
public interface IRequestValidator<in TRequest>
{
    /// <summary>Validates <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The validation errors; empty when the request is valid.</returns>
    ValueTask<IReadOnlyList<Error>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}
