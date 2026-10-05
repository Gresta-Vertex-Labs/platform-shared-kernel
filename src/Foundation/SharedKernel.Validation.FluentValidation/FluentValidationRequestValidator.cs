using FluentValidation;
using FluentValidation.Results;
using SharedKernel.Application.Validation;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Validation.FluentValidation;

/// <summary>
/// Runs every FluentValidation <see cref="IValidator{T}"/> registered for <typeparamref name="TRequest"/>
/// as one kernel <see cref="IRequestValidator{TRequest}"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <remarks>
/// <para>
/// Registered open-generic by <see cref="FluentValidationServiceCollectionExtensions.AddFluentValidationRequestValidators"/>.
/// The validators run sequentially, never concurrently, because a validator may use a scoped
/// resource such as a <c>DbContext</c>.
/// </para>
/// <para>
/// Each failure becomes one validation <see cref="Error"/>: its code is the failure's
/// <c>ErrorCode</c> (the code set with <c>WithErrorCode(...)</c>, or FluentValidation's default, the
/// validator name such as <c>NotEmptyValidator</c>; <see cref="ErrorCodes.Validation.Failed"/> when a
/// hand-built failure has none), its message is the formatted <c>ErrorMessage</c>, and its
/// <see cref="Error.MessageArguments"/> hold the message placeholder values plus
/// <see cref="ErrorArgumentNames.PropertyPath"/> (the full path, such as <c>Accounts[0].Iban</c>) and
/// <see cref="ErrorArgumentNames.PropertyName"/>. The attempted value (FluentValidation's
/// <c>PropertyValue</c> placeholder) is always left out: it can be a card number or a national
/// identifier, and must never travel inside an error.
/// </para>
/// </remarks>
/// <param name="validators">The FluentValidation validators registered for <typeparamref name="TRequest"/>.</param>
public sealed class FluentValidationRequestValidator<TRequest>(IEnumerable<IValidator<TRequest>> validators)
    : IRequestValidator<TRequest>
{
    // FluentValidation's placeholder for the attempted value. Never copied into an error.
    private const string AttemptedValuePlaceholder = "PropertyValue";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Error>> ValidateAsync(TRequest request, CancellationToken cancellationToken)
    {
        List<Error>? errors = null;

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsValid)
                continue;

            errors ??= [];
            foreach (var failure in result.Errors)
            {
                if (failure is not null)
                    errors.Add(ToError(failure));
            }
        }

        return errors is null ? [] : errors;
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
