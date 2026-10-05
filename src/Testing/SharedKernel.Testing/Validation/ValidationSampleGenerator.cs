using Bogus;
using SharedKernel.Validation;

namespace SharedKernel.Testing.Validation;

/// <summary>
/// Checksum-correct valid/invalid sample values for the identifier types in
/// <c>SharedKernel.Validation</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>Valid*</c> member computes a genuinely correct check digit/checksum (ISO 13616 mod-97
/// for IBAN, Luhn/mod-10 for PAN, the TCKN algorithm for Turkish national IDs) rather than
/// returning a hardcoded literal — so a future change to a check-digit table cannot silently
/// desynchronize this generator from the real validator it targets. Every <c>Invalid*</c> member
/// deliberately corrupts exactly one character of an otherwise-valid sample so it fails only the
/// intended check, never an unrelated format rule (e.g. <see cref="InvalidIban"/> stays the
/// correct length for its country and keeps a recognized country prefix; only the checksum
/// breaks).
/// </para>
/// <para>
/// Deterministic via a package-local, fixed-seed <see cref="Faker"/> instance — independent of
/// whether the consuming test assembly has called <c>FakerSeeding.Apply()</c>.
/// </para>
/// </remarks>
public static class ValidationSampleGenerator
{
    private static readonly Faker Faker = new() { Random = new Randomizer(8675309) };

    // The national account number (BBAN) structure of each supported country, from the SWIFT IBAN
    // registry: n = digit, a = upper-case letter, c = letter or digit. Public structural data, not
    // the checksum under test.
    private static readonly IReadOnlyDictionary<string, string> IbanBbanFormats = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DE"] = "18n",
        ["GB"] = "4a,14n",
        ["FR"] = "10n,11c,2n",
        ["ES"] = "20n",
        ["IT"] = "1a,10n,12c",
        ["NL"] = "4a,10n",
        ["TR"] = "5n,1n,16c",
    };

    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string IbanAlphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";

    // ── IBAN (ISO 13616 mod-97) ─────────────────────────────────────────────

    /// <summary>Generates a checksum-valid IBAN for <paramref name="countryCode"/>.</summary>
    /// <param name="countryCode">
    /// The ISO 3166-1 alpha-2 country code to generate for. Supported: <c>DE</c>, <c>GB</c>,
    /// <c>FR</c>, <c>ES</c>, <c>IT</c>, <c>NL</c>, <c>TR</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="countryCode"/> is not supported.</exception>
    public static string ValidIban(string countryCode = "DE")
    {
        if (!IbanBbanFormats.TryGetValue(countryCode, out var format))
        {
            throw new ArgumentException($"Unsupported IBAN country code '{countryCode}'.", nameof(countryCode));
        }

        var bban = string.Concat(format.Split(',').Select(segment =>
            Faker.Random.String2(
                int.Parse(segment[..^1], System.Globalization.CultureInfo.InvariantCulture),
                segment[^1] switch { 'n' => Digits, 'a' => Letters, _ => IbanAlphanumeric })));
        var checkDigits = ComputeIbanCheckDigits(countryCode, bban);
        return $"{countryCode}{checkDigits}{bban}";
    }

    /// <summary>
    /// Generates a structurally well-formed but checksum-INVALID IBAN — same country prefix and
    /// length as <see cref="ValidIban"/>, with one BBAN digit deliberately mutated so only the
    /// mod-97 check fails.
    /// </summary>
    public static string InvalidIban(string countryCode = "DE")
    {
        var valid = ValidIban(countryCode);
        return MutateLastAlphanumericCharacter(valid);
    }

    private static string ComputeIbanCheckDigits(string countryCode, string bban)
    {
        // ISO 13616: rearrange BBAN + countryCode + "00", compute mod-97, check = 98 - remainder.
        var rearranged = bban + countryCode + "00";
        var remainder = Mod97(rearranged);
        var checkDigits = 98 - remainder;
        return checkDigits.ToString("D2");
    }

    private static int Mod97(string value)
    {
        var remainder = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                remainder = (remainder * 10 + (c - '0')) % 97;
            }
            else
            {
                var letterValue = c - 'A' + 10; // A=10 .. Z=35
                remainder = (remainder * 100 + letterValue) % 97;
            }
        }

        return remainder;
    }

    // ── BIC/SWIFT (format + ISO 3166-1 country segment) ─────────────────────

    /// <summary>Generates a well-formed 11-character BIC with a recognized country segment.</summary>
    public static string ValidBic()
    {
        var bankCode = Faker.Random.String2(4, "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        var locationCode = Faker.Random.String2(2, IbanAlphanumeric);
        var branchCode = Faker.Random.String2(3, IbanAlphanumeric);
        return $"{bankCode}DE{locationCode}{branchCode}";
    }

    /// <summary>Generates a structurally invalid BIC — an unrecognized two-letter country segment.</summary>
    public static string InvalidBic()
    {
        var bankCode = Faker.Random.String2(4, "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        var locationCode = Faker.Random.String2(2, IbanAlphanumeric);
        var branchCode = Faker.Random.String2(3, IbanAlphanumeric);
        // "ZZ" is not a recognized ISO 3166-1 alpha-2 code.
        return $"{bankCode}ZZ{locationCode}{branchCode}";
    }

    // ── PAN (Luhn / mod-10) ──────────────────────────────────────────────────

    /// <summary>Generates a Luhn-valid PAN for <paramref name="network"/>.</summary>
    public static string ValidPan(CardNetwork network = CardNetwork.Visa)
    {
        var (prefix, length) = network switch
        {
            CardNetwork.Visa => ("4", 16),
            CardNetwork.Mastercard => ("55", 16),
            CardNetwork.AmericanExpress => ("34", 15),
            CardNetwork.Discover => ("6011", 16),
            CardNetwork.Jcb => ("3528", 16),
            CardNetwork.UnionPay => ("62", 16),
            CardNetwork.DinersClub => ("36", 14),
            CardNetwork.Maestro => ("6759", 16),
            CardNetwork.Mir => ("2200", 16),
            CardNetwork.Troy => ("9792", 16),
            _ => ("4", 16),
        };

        var bodyLength = length - prefix.Length - 1; // reserve the trailing Luhn check digit
        var body = prefix + Faker.Random.String2(bodyLength, Digits);
        var checkDigit = ComputeLuhnCheckDigit(body);
        return body + checkDigit;
    }

    /// <summary>
    /// Generates a structurally well-formed but Luhn-INVALID PAN — same length/network prefix as
    /// <see cref="ValidPan"/>, with the trailing check digit deliberately mutated.
    /// </summary>
    public static string InvalidPan(CardNetwork network = CardNetwork.Visa)
    {
        var valid = ValidPan(network);
        return MutateLastAlphanumericCharacter(valid);
    }

    private static char ComputeLuhnCheckDigit(string bodyWithoutCheckDigit)
    {
        var sum = 0;
        var doubleDigit = true; // the check digit itself occupies the next "doubled" position

        for (var i = bodyWithoutCheckDigit.Length - 1; i >= 0; i--)
        {
            var digit = bodyWithoutCheckDigit[i] - '0';

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return (char)('0' + checkDigit);
    }

    // ── ISO 4217 currency code ───────────────────────────────────────────────

    private static readonly string[] KnownCurrencyCodes = ["USD", "EUR", "GBP", "JPY", "CHF", "CAD", "AUD", "TRY"];

    /// <summary>Generates a recognized ISO 4217 alphabetic currency code.</summary>
    public static string ValidCurrencyCode() => Faker.PickRandom(KnownCurrencyCodes);

    /// <summary>Generates an unrecognized 3-letter currency-code-shaped value.</summary>
    public static string InvalidCurrencyCode() => "ZZZ";

    // ── ISO 3166-1 alpha-2 country code ──────────────────────────────────────

    private static readonly string[] KnownCountryCodes = ["US", "DE", "GB", "FR", "TR", "JP", "CA", "AU"];

    /// <summary>Generates a recognized ISO 3166-1 alpha-2 country code.</summary>
    public static string ValidCountryCode() => Faker.PickRandom(KnownCountryCodes);

    /// <summary>Generates an unrecognized 2-letter country-code-shaped value.</summary>
    public static string InvalidCountryCode() => "ZZ";

    // ── E.164 phone number ────────────────────────────────────────────────────

    /// <summary>Generates a well-formed E.164 phone number.</summary>
    public static string ValidE164Phone() => "+1" + Faker.Random.String2(10, Digits);

    /// <summary>
    /// Generates a phone-number-shaped value that fails E.164 format validation — a leading zero
    /// immediately after the <c>+</c>, which the format explicitly disallows.
    /// </summary>
    public static string InvalidE164Phone() => "+0" + Faker.Random.String2(9, Digits);

    // ── VAT / tax number (country format + check digit) ──────────────────────

    // Digit-only VAT formats whose check digit is the last digit: the prefix and the total digits.
    private static readonly IReadOnlyDictionary<string, int> VatDigitCounts = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["DE"] = 9,
        ["TR"] = 10,
        ["PL"] = 10,
        ["DK"] = 8,
        ["FI"] = 8,
        ["PT"] = 9,
        ["EE"] = 9,
    };

    /// <summary>Generates a VAT number with a correct check digit for <paramref name="countryCode"/>, prefix included.</summary>
    /// <param name="countryCode">
    /// The VAT prefix. Supported: <c>DE</c>, <c>TR</c> (VKN), <c>PL</c>, <c>DK</c>, <c>FI</c>, <c>PT</c>, <c>EE</c>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="countryCode"/> is not supported.</exception>
    public static string ValidVat(string countryCode = "DE")
    {
        string prefix = countryCode.ToUpperInvariant();
        if (!VatDigitCounts.TryGetValue(prefix, out var digits))
        {
            throw new ArgumentException($"Unsupported VAT country code '{countryCode}'.", nameof(countryCode));
        }

        // Draw a body (first digit non-zero, as several countries require) and take the check
        // digit the real validator accepts; some bodies have none, so draw again.
        while (true)
        {
            string body = Faker.Random.Int(1, 9).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + Faker.Random.String2(digits - 2, Digits);
            for (var check = 0; check <= 9; check++)
            {
                string candidate = prefix + body + check;
                if (VatNumber.IsValid(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Generates a VAT number with the right format but a wrong check digit: <see cref="ValidVat"/>
    /// with its last digit changed.
    /// </summary>
    /// <param name="countryCode">The VAT prefix; see <see cref="ValidVat"/>.</param>
    public static string InvalidVat(string countryCode = "DE") => MutateLastAlphanumericCharacter(ValidVat(countryCode));

    // ── National ID (TCKN — Turkey, the platform's built-in default) ────────

    /// <summary>Generates a checksum-valid Turkish TCKN national identity number.</summary>
    /// <param name="countryCode">Only <c>TR</c> is currently supported.</param>
    /// <exception cref="ArgumentException"><paramref name="countryCode"/> is not <c>TR</c>.</exception>
    public static string ValidNationalId(string countryCode = "TR")
    {
        EnsureTckn(countryCode);

        Span<int> digits = stackalloc int[11];
        digits[0] = Faker.Random.Int(1, 9); // d1 must be non-zero
        for (var i = 1; i < 9; i++)
        {
            digits[i] = Faker.Random.Int(0, 9);
        }

        var oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var evenSum = digits[1] + digits[3] + digits[5] + digits[7];

        digits[9] = Mod10((oddSum * 7) - evenSum);
        digits[10] = Mod10(oddSum + evenSum + digits[9]);

        Span<char> chars = stackalloc char[11];
        for (var i = 0; i < 11; i++)
        {
            chars[i] = (char)('0' + digits[i]);
        }

        return new string(chars);
    }

    /// <summary>
    /// Generates a structurally well-formed but checksum-INVALID TCKN — the trailing check digit
    /// is deliberately mutated so only the final checksum comparison fails.
    /// </summary>
    public static string InvalidNationalId(string countryCode = "TR")
    {
        var valid = ValidNationalId(countryCode);
        return MutateLastAlphanumericCharacter(valid);
    }

    private static void EnsureTckn(string countryCode)
    {
        if (!string.Equals(countryCode, "TR", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unsupported national-id country code '{countryCode}' — only 'TR' (TCKN) is currently supported.",
                nameof(countryCode));
        }
    }

    private static int Mod10(int value) => ((value % 10) + 10) % 10;

    // ── Shared corruption helper ─────────────────────────────────────────────

    /// <summary>
    /// Increments the last digit (mod 10, wrapping) of an otherwise-valid checksum-bearing sample,
    /// guaranteeing the checksum breaks while every structural property (length, character set,
    /// prefix) stays unchanged.
    /// </summary>
    private static string MutateLastAlphanumericCharacter(string value)
    {
        var chars = value.ToCharArray();
        var lastIndex = chars.Length - 1;
        var last = chars[lastIndex];

        chars[lastIndex] = char.IsAsciiDigit(last)
            ? (char)('0' + ((last - '0' + 1) % 10))
            : (char)('A' + ((last - 'A' + 1) % 26));

        return new string(chars);
    }
}
