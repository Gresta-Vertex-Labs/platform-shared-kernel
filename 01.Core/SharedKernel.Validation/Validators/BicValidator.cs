using System.Text.RegularExpressions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent BIC/SWIFT (ISO 9362 Business Identifier Code) format validator.
/// </summary>
/// <remarks>
/// A BIC is 8 or 11 characters: a 4-letter institution code, a 2-letter ISO 3166-1 country
/// code, a 2-character alphanumeric location code, and an optional 3-character alphanumeric
/// branch code. The embedded country-code segment is additionally checked against
/// <see cref="IsoCountryValidator"/>.
/// </remarks>
public static class BicValidator
{
    private static readonly Regex Pattern = new(
        @"^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed BIC.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> as a BIC (case-insensitive).</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Bic.InvalidFormat, "BIC must not be null or empty.");
        }

        string normalized = value.Trim().ToUpperInvariant();

        if (!Pattern.IsMatch(normalized))
        {
            return Error.Validation(ValidationErrorCodes.Bic.InvalidFormat,
                "BIC must be 8 or 11 characters: a 4-letter bank code, a 2-letter country code, " +
                "a 2-character location code, and an optional 3-character branch code.");
        }

        string countryCode = normalized.Substring(4, 2);

        return IsoCountryValidator.IsValid(countryCode)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Bic.InvalidFormat, $"'{countryCode}' is not a recognized ISO 3166-1 country code.");
    }
}
