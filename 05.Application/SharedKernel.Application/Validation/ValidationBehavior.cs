using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Runs all registered FluentValidation validators for <typeparamref name="TRequest"/> before the
/// inner pipeline executes.
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Runs every registered validator <b>sequentially</b> — not <c>Task.WhenAll</c> — since a
/// validator may use a scoped resource (e.g. a <c>DbContext</c>) that is not safe for concurrent
/// use. Every failure across every validator is collected and aggregated into a single
/// <c>Error.Validation(errors)</c>. The aggregate is returned as a
/// <c>Result.Failure</c>/<c>Result&lt;T&gt;.Failure</c> — <b>never thrown</b>.
/// </para>
/// <para>
/// Each failure becomes one child <see cref="Error"/>:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <see cref="Error.Code"/> is <c>failure.ErrorCode</c>: the code set with
///   <c>WithErrorCode(...)</c>, or FluentValidation's default, the validator name such as
///   <c>NotEmptyValidator</c>. A failure without a code, which only happens when a
///   <c>ValidationFailure</c> is constructed by hand, gets
///   <see cref="ErrorCodes.Validation.Failed"/>, the generic validation code, rather than a
///   specific code that would claim a cause nobody reported.
///   </description></item>
///   <item><description>
///   <see cref="Error.Message"/> is <c>failure.ErrorMessage</c>, already formatted by
///   FluentValidation.
///   </description></item>
///   <item><description>
///   <see cref="Error.MessageArguments"/> holds FluentValidation's message placeholder values, so
///   the HTTP boundary can translate the message by its code and fill in the same values. It also
///   holds <see cref="ErrorArgumentNames.PropertyPath"/>, the full path of the failing field such
///   as <c>Accounts[0].Iban</c>, which <c>14.Presentation</c> uses as the key of the
///   <c>ProblemDetails</c> <c>errors</c> and <c>errorCodes</c> maps, and
///   <see cref="ErrorArgumentNames.PropertyName"/>, the field's display name. The attempted value
///   (FluentValidation's <c>PropertyValue</c> placeholder) is always left out: it can be a card
///   number or a national identifier, and must never travel inside an error.
///   </description></item>
/// </list>
/// <para>
/// When zero validators are registered for <typeparamref name="TRequest"/>, <c>next()</c> is
/// called immediately — <see cref="IEnumerable{IValidator}"/> is empty (never null) by DI
/// convention, so this is a zero-cost no-op, not a missing-registration error. Applies to
/// <b>both</b> commands and queries — <typeparamref name="TRequest"/> is constrained only to
/// <see cref="IRequest{TResponse}"/>.
/// </para>
/// </remarks>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // FluentValidation's placeholder for the attempted value. Never copied into an error.
    private const string AttemptedValuePlaceholder = "PropertyValue";

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

                errors.Add(ToError(failure));
            }
        }

        if (errors is { Count: > 0 })
            return FailureResponse.Create<TResponse>(Error.Validation(errors));

        return await next().ConfigureAwait(false);
    }

    private static Error ToError(ValidationFailure failure)
    {
        var code = string.IsNullOrWhiteSpace(failure.ErrorCode)
            ? ErrorCodes.Validation.Failed
            : failure.ErrorCode;

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (failure.FormattedMessagePlaceholderValues is { } placeholders)
        {
            foreach (var (name, value) in placeholders)
            {
                if (!string.Equals(name, AttemptedValuePlaceholder, StringComparison.Ordinal))
                    arguments[name] = value;
            }
        }

        arguments[ErrorArgumentNames.PropertyPath] = failure.PropertyName;
        arguments.TryAdd(ErrorArgumentNames.PropertyName, failure.PropertyName);

        return Error.Validation(code, failure.ErrorMessage) with { MessageArguments = arguments };
    }
}
