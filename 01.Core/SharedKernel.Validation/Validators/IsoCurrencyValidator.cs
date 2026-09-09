using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;

namespace SharedKernel.Validation.Validators;

/// <summary>
/// Culture-independent ISO 4217 currency code validator.
/// </summary>
/// <remarks>
/// Backed by a fixed, compile-time <see cref="HashSet{T}"/> of currently-active ISO 4217
/// alphabetic currency codes — not a runtime lookup against any external registry. The table
/// should be kept in sync with ISO 4217 maintenance-agency updates as codes are added, retired,
/// or redenominated.
/// </remarks>
public static class IsoCurrencyValidator
{
    /// <summary>
    /// A last-reviewed marker for <see cref="Codes"/>, NOT a formal ISO 4217 registry version
    /// number — the ISO 4217 maintenance agency does not publish a single canonical "version" the
    /// way software does. States when this table was last verified against published ISO 4217
    /// currency-code references. Review this table, and update this constant, whenever a work
    /// order touches <see cref="IsoCurrencyValidator"/>.
    /// </summary>
    public const string RegistryAsOf = "Reviewed WO-067/P-443, 2026-09-02";

    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        "CAD", "CDF", "CHF", "CLP", "CNY", "COP", "CRC", "CUP", "CVE", "CZK",
        "DJF", "DKK", "DOP", "DZD",
        "EGP", "ERN", "ETB", "EUR",
        "FJD", "FKP",
        "GBP", "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD",
        "HKD", "HNL", "HTG", "HUF",
        "IDR", "ILS", "INR", "IQD", "IRR", "ISK",
        "JMD", "JOD", "JPY",
        "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT",
        "LAK", "LBP", "LKR", "LRD", "LSL", "LYD",
        "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN",
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD",
        "OMR",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG",
        "QAR",
        "RON", "RSD", "RUB", "RWF",
        "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SYP", "SZL",
        "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD", "TWD", "TZS",
        "UAH", "UGX", "USD", "UYU", "UZS",
        "VES", "VND", "VUV",
        "WST",
        "XAF", "XCD", "XOF", "XPF",
        "YER",
        "ZAR", "ZMW", "ZWL",
    };

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is a recognized ISO 4217 currency code.</summary>
    public static bool IsValid(string? value) => Validate(value).IsSuccess;

    /// <summary>Validates <paramref name="value"/> as an ISO 4217 alphabetic currency code (case-insensitive).</summary>
    public static Result Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Error.Validation(ValidationErrorCodes.Currency.UnknownCode, "Currency code must not be null or empty.");
        }

        string normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length != 3 || !IsAsciiUpperLetters(normalized))
        {
            return Error.Validation(ValidationErrorCodes.Currency.UnknownCode, "Currency code must be a 3-letter ISO 4217 code.");
        }

        return Codes.Contains(normalized)
            ? Result.Success()
            : Error.Validation(ValidationErrorCodes.Currency.UnknownCode, $"'{normalized}' is not a recognized ISO 4217 currency code.");
    }

    private static bool IsAsciiUpperLetters(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiLetterUpper(c))
            {
                return false;
            }
        }

        return true;
    }
}
