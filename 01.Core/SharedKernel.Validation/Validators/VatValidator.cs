using System.Text.RegularExpressions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// A BASELINE, cross-jurisdiction VAT/tax-identifier format check.
/// </summary>
/// <remarks>
/// <para>
/// <b>This validator is deliberately NOT exhaustive.</b> VAT/tax-identifier formats vary
/// enormously by country — different lengths, different embedded checksum algorithms (or none
/// at all), and different allowances for letters within the numeric body (e.g. the Netherlands'
/// VAT number embeds a literal <c>"B"</c> before a 2-digit company sub-number). This validator
/// only confirms a value matches the common shape used by most national/EU-style VAT numbers: a
/// 2-letter country prefix followed by 2-12 further alphanumeric characters. It performs NO
/// per-country checksum validation. Do not treat a passing result as proof the number is a real,
/// registered VAT identifier — only that it is not obviously malformed.
/// </para>
/// </remarks>
public static class VatValidator
{
    private static readonly Regex Pattern = new(
        @"^[A-Z]{2}[A-Z0-9]{2,12}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> matches the baseline VAT number format.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> against the baseline VAT/tax-identifier format (see remarks).</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Vat.InvalidFormat, "VAT number must not be null or empty.");
        }

        string normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        return Pattern.IsMatch(normalized)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Vat.InvalidFormat,
                "VAT number does not match the baseline [2-letter country prefix][2-12 alphanumeric] format.");
    }
}
