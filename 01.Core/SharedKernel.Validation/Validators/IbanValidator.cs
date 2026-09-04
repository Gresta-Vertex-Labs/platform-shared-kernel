using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent IBAN (International Bank Account Number) format validator.
/// </summary>
/// <remarks>
/// <para>
/// Validation is two-stage: (1) the value's country prefix is looked up in a per-country
/// length table and the value's length must match exactly — IBANs are NOT a fixed length
/// across all countries; (2) the ISO 13616 mod-97 check-digit algorithm (rearrange the first
/// four characters to the end, map letters to two-digit numeric values A=10..Z=35, and confirm
/// the resulting numeric string is congruent to 1 modulo 97).
/// </para>
/// <para>
/// The length table covers the countries most commonly represented in published SWIFT/ISO
/// IBAN registry references. An unrecognized two-letter prefix fails validation with
/// <see cref="ValidationErrorCodes.Iban.InvalidFormat"/> rather than throwing — this is a
/// closed, maintained table, not an attempt at 100% coverage of every jurisdiction that has
/// ever registered an IBAN format.
/// </para>
/// </remarks>
public static class IbanValidator
{
    // Per-country total IBAN length (2-letter country code + 2 check digits + BBAN), sourced
    // from the published SWIFT/ISO 13616 IBAN registry structural lengths.
    private static readonly IReadOnlyDictionary<string, int> LengthsByCountry = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["AD"] = 24, ["AE"] = 23, ["AL"] = 28, ["AT"] = 20, ["AZ"] = 28,
        ["BA"] = 20, ["BE"] = 16, ["BG"] = 22, ["BH"] = 22, ["BR"] = 29,
        ["BY"] = 28, ["CH"] = 21, ["CR"] = 22, ["CY"] = 28, ["CZ"] = 24,
        ["DE"] = 22, ["DK"] = 18, ["DO"] = 28, ["EE"] = 20, ["EG"] = 29,
        ["ES"] = 24, ["FI"] = 18, ["FO"] = 18, ["FR"] = 27, ["GB"] = 22,
        ["GE"] = 22, ["GI"] = 23, ["GL"] = 18, ["GR"] = 27, ["GT"] = 28,
        ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IL"] = 23, ["IQ"] = 23,
        ["IS"] = 26, ["IT"] = 27, ["JO"] = 30, ["KW"] = 30, ["KZ"] = 20,
        ["LB"] = 28, ["LC"] = 32, ["LI"] = 21, ["LT"] = 20, ["LU"] = 20,
        ["LV"] = 21, ["LY"] = 25, ["MC"] = 27, ["MD"] = 24, ["ME"] = 22,
        ["MK"] = 19, ["MR"] = 27, ["MT"] = 31, ["MU"] = 30, ["NL"] = 18,
        ["NO"] = 15, ["PK"] = 24, ["PL"] = 28, ["PS"] = 29, ["PT"] = 25,
        ["QA"] = 29, ["RO"] = 24, ["RS"] = 22, ["SA"] = 24, ["SC"] = 31,
        ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27, ["ST"] = 25,
        ["SV"] = 28, ["TL"] = 23, ["TN"] = 24, ["TR"] = 26, ["UA"] = 29,
        ["VA"] = 22, ["VG"] = 24, ["XK"] = 20,
    };

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a well-formed, checksum-valid IBAN.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>
    /// Validates <paramref name="value"/> as an IBAN. Accepts values with or without spaces
    /// (e.g. <c>"DE89 3704 0044 0532 0130 00"</c> and <c>"DE89370400440532013000"</c> both validate).
    /// </summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Iban.InvalidFormat, "IBAN must not be null or empty.");
        }

        string normalized = Normalize(value);

        if (normalized.Length < 4
            || !char.IsAsciiLetter(normalized[0]) || !char.IsAsciiLetter(normalized[1])
            || !char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3]))
        {
            return Error.Validation(ValidationErrorCodes.Iban.InvalidFormat,
                "IBAN must start with a 2-letter country code followed by 2 check digits.");
        }

        string countryCode = normalized[..2];

        if (!LengthsByCountry.TryGetValue(countryCode, out int expectedLength))
        {
            return Error.Validation(ValidationErrorCodes.Iban.InvalidFormat,
                $"'{countryCode}' is not a recognized IBAN country code.");
        }

        if (normalized.Length != expectedLength)
        {
            return Error.Validation(ValidationErrorCodes.Iban.InvalidLength,
                $"IBAN for country '{countryCode}' must be {expectedLength} characters; got {normalized.Length}.");
        }

        foreach (char c in normalized)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c))
            {
                return Error.Validation(ValidationErrorCodes.Iban.InvalidFormat,
                    "IBAN must contain only letters and digits.");
            }
        }

        return Mod97CheckPasses(normalized)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Iban.InvalidCheckDigit,
                "IBAN failed the ISO 13616 mod-97 check-digit validation.");
    }

    private static string Normalize(string value)
    {
        Span<char> buffer = value.Length <= 64 ? stackalloc char[value.Length] : new char[value.Length];
        int written = 0;

        foreach (char c in value)
        {
            if (c is ' ' or '-')
            {
                continue;
            }

            buffer[written++] = char.ToUpperInvariant(c);
        }

        return new string(buffer[..written]);
    }

    // ISO 13616 mod-97 check: move the first 4 characters to the end, expand each letter to
    // its two-digit numeric value (A=10..Z=35), and confirm the resulting big integer mod 97 == 1.
    // Computed incrementally (never materializing the full numeric string as a BigInteger) via the
    // standard "process digit-by-digit, folding the running remainder" technique.
    private static bool Mod97CheckPasses(string iban)
    {
        int remainder = 0;

        for (int i = 4; i < iban.Length + 4; i++)
        {
            char c = iban[i % iban.Length];

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
