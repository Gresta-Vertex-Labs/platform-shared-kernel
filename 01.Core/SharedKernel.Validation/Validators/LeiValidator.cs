using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent LEI (Legal Entity Identifier, ISO 17442) format validator.
/// </summary>
/// <remarks>
/// <para>
/// An LEI is a fixed 20-character alphanumeric identifier: a 4-character Local Operating Unit
/// (LOU) prefix (characters 1-4), a 14-character entity-specific reference assigned by that LOU
/// (characters 5-18), and 2 numeric check digits (characters 19-20).
/// </para>
/// <para>
/// The check digits are validated via the ISO/IEC 7064 MOD 97-10 checksum, applied to the WHOLE
/// 20-character string with letters expanded to their two-digit numeric value (A=10..Z=35) —
/// unlike <see cref="IbanValidator"/>, no characters are moved/rearranged before the mod-97 pass;
/// a valid LEI's full 20-character numeric expansion is congruent to 1 modulo 97. This was
/// verified empirically against five real, GLEIF-published LEI codes at implementation time (see
/// this validator's test suite for the sourced vectors) before being encoded here.
/// </para>
/// </remarks>
public static class LeiValidator
{
    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed, checksum-valid LEI.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> as an LEI. Case-insensitive; leading/trailing whitespace is trimmed.</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Lei.InvalidFormat, "LEI must not be null or empty.");
        }

        string normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 20)
        {
            return Error.Validation(ValidationErrorCodes.Lei.InvalidFormat, "LEI must be exactly 20 characters.");
        }

        for (int i = 0; i < 18; i++)
        {
            if (!char.IsAsciiLetterUpper(normalized[i]) && !char.IsAsciiDigit(normalized[i]))
            {
                return Error.Validation(ValidationErrorCodes.Lei.InvalidFormat,
                    "The first 18 characters of an LEI (LOU prefix + entity-specific reference) must be letters or digits.");
            }
        }

        if (!char.IsAsciiDigit(normalized[18]) || !char.IsAsciiDigit(normalized[19]))
        {
            return Error.Validation(ValidationErrorCodes.Lei.InvalidFormat,
                "The last 2 characters of an LEI (the check digits) must be numeric.");
        }

        return Mod97CheckPasses(normalized)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Lei.InvalidCheckDigit,
                "LEI failed the ISO/IEC 7064 MOD 97-10 check-digit validation.");
    }

    // ISO/IEC 7064 MOD 97-10 over the full 20-character string (no rearrangement, unlike IBAN):
    // letters expand to their two-digit numeric value (A=10..Z=35); a valid LEI's numeric
    // expansion is congruent to 1 modulo 97. Computed incrementally (never materializing the
    // full numeric string as a BigInteger), mirroring IbanValidator's own folding technique.
    private static bool Mod97CheckPasses(string lei)
    {
        int remainder = 0;

        foreach (char c in lei)
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

        return remainder == 1;
    }
}
