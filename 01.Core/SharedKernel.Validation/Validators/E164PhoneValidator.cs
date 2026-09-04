using System.Text.RegularExpressions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent E.164 phone number format validator.
/// </summary>
/// <remarks>
/// E.164 requires a leading <c>+</c>, followed by a non-zero digit, followed by up to 14
/// further digits (15 digits total, maximum). This is a FORMAT check only — it does not verify
/// the number is actually assigned, dialable, or valid for its claimed country calling code.
/// </remarks>
public static class E164PhoneValidator
{
    private static readonly Regex Pattern = new(
        @"^\+[1-9]\d{1,14}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed E.164 phone number.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> as an E.164-formatted phone number (e.g. <c>"+14155550100"</c>).</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Phone.InvalidFormat, "Phone number must not be null or empty.");
        }

        return Pattern.IsMatch(value)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Phone.InvalidFormat,
                "Phone number must be in E.164 format: a leading '+', a non-zero digit, and up to 14 further digits.");
    }
}
