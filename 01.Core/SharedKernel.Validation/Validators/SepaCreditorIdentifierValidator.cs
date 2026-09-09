using System.Globalization;
using System.Text.RegularExpressions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent SEPA Creditor Identifier (SEPA Direct Debit "Gläubiger-ID") format validator.
/// </summary>
/// <remarks>
/// <para>
/// A SEPA Creditor Identifier is: a 2-letter ISO 3166-1 country code (positions 1-2), 2 numeric
/// check digits (positions 3-4), a 3-character alphanumeric Creditor Business Code — the literal
/// <c>"ZZZ"</c> when unused (positions 5-7) — and a 1-to-28-character alphanumeric national
/// creditor identifier (positions 8 onward), for an overall maximum length of 35 characters, per
/// the European Payments Council's published Creditor Identifier Overview.
/// </para>
/// <para>
/// The check digits are validated via the ISO/IEC 7064 MOD 97-10 checksum, computed over the
/// string <c>nationalIdentifier + countryCode + "00"</c> — the Creditor Business Code is
/// deliberately EXCLUDED from the checksum input; changing it never changes the check digits.
/// This mirrors <see cref="IbanValidator"/>'s own rearrange-then-mod-97 technique with a
/// different rearrangement: <c>expectedCheckDigits = 98 - (mod-97 remainder of that string)</c>.
/// Verified empirically against a published, widely-cited test Creditor Identifier
/// (<c>DE98ZZZ09999999999</c>) at implementation time before being encoded here.
/// </para>
/// </remarks>
public static class SepaCreditorIdentifierValidator
{
    private static readonly Regex Pattern = new(
        @"^[A-Z]{2}[0-9]{2}[A-Z0-9]{3}[A-Z0-9]{1,28}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed, checksum-valid SEPA Creditor Identifier.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> as a SEPA Creditor Identifier. Accepts values with or without spaces.</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat,
                "SEPA Creditor Identifier must not be null or empty.");
        }

        string normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

        if (normalized.Length > 35 || !Pattern.IsMatch(normalized))
        {
            return Error.Validation(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat,
                "SEPA Creditor Identifier must be a 2-letter country code, 2 check digits, a 3-character " +
                "business code, and a 1-28 character national identifier (35 characters maximum).");
        }

        string countryCode = normalized[..2];
        string checkDigits = normalized.Substring(2, 2);
        string nationalIdentifier = normalized[7..];

        if (!IsoCountryValidator.IsValid(countryCode))
        {
            return Error.Validation(ValidationErrorCodes.SepaCreditorIdentifier.InvalidFormat,
                $"'{countryCode}' is not a recognized ISO 3166-1 country code.");
        }

        string expectedCheckDigits = ComputeCheckDigits(nationalIdentifier, countryCode);

        return string.Equals(checkDigits, expectedCheckDigits, StringComparison.Ordinal)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.SepaCreditorIdentifier.InvalidCheckDigit,
                "SEPA Creditor Identifier failed the ISO/IEC 7064 MOD 97-10 check-digit validation.");
    }

    // Per the EPC Creditor Identifier Overview: check digits = 98 - mod97(nationalIdentifier +
    // countryCode + "00"). The Creditor Business Code (positions 5-7) is deliberately excluded.
    private static string ComputeCheckDigits(string nationalIdentifier, string countryCode)
    {
        int remainder = Mod97(nationalIdentifier + countryCode + "00");
        int checkDigits = 98 - remainder;
        return checkDigits.ToString("D2", CultureInfo.InvariantCulture);
    }

    // ISO/IEC 7064 MOD 97-10, folded incrementally exactly like IbanValidator's own helper —
    // letters expand to their two-digit numeric value (A=10..Z=35).
    private static int Mod97(string value)
    {
        int remainder = 0;

        foreach (char c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                remainder = (remainder * 10 + (c - '0')) % 97;
            }
            else
            {
                int letterValue = c - 'A' + 10; // A=10 .. Z=35
                remainder = (remainder * 100 + letterValue) % 97;
            }
        }

        return remainder;
    }
}
