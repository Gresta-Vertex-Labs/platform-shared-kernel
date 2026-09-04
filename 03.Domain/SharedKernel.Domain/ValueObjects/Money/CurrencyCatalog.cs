namespace SharedKernel.Domain.ValueObjects.Money;

/// <summary>
/// Fixed, compile-time lookup table of ISO 4217 alpha-3 currency codes and their minor-unit
/// (decimal-place) exponent.
/// </summary>
/// <remarks>
/// <para>
/// WO-066/P-439. This is a fixed, in-memory dataset — never I/O-backed, never refreshed at
/// runtime. Adding, removing, or correcting a currency's exponent requires a new
/// <c>SharedKernel.Domain</c> package version, not a runtime configuration change.
/// </para>
/// <para>
/// The default minor-unit exponent is <c>2</c> (e.g. USD, EUR). Two documented exception sets
/// override the default: zero-decimal currencies (e.g. JPY) and three-decimal currencies
/// (e.g. BHD). Every code covered here is an actively circulating national or supranational
/// transactional currency — collateral/index/bond-market units (e.g. CLF, USN, XDR) and
/// precious-metal codes (e.g. XAU, XAG) are intentionally out of scope, since they are not
/// used as a <see cref="Money"/> transactional currency in this platform's domains.
/// </para>
/// </remarks>
public static class CurrencyCatalog
{
    private const int DefaultMinorUnitDigits = 2;
    private const int ZeroMinorUnitDigits = 0;
    private const int ThreeMinorUnitDigits = 3;

    /// <summary>
    /// Zero-decimal currencies — e.g. Japanese Yen, which has no minor (sub-unit) denomination.
    /// </summary>
    private static readonly string[] ZeroDecimalCodes =
    [
        "JPY", "KRW", "VND", "ISK", "CLP", "PYG", "UGX", "RWF",
        "XOF", "XAF", "XPF", "KMF", "GNF", "DJF", "VUV",
    ];

    /// <summary>
    /// Three-decimal currencies — mostly Gulf-state dinars, whose minor unit subdivides
    /// further than the usual two decimal places.
    /// </summary>
    private static readonly string[] ThreeDecimalCodes =
    [
        "BHD", "KWD", "OMR", "JOD", "TND", "LYD", "IQD",
    ];

    /// <summary>
    /// Every other actively circulating ISO 4217 alpha-3 code at the platform-default two
    /// decimal places.
    /// </summary>
    private static readonly string[] TwoDecimalCodes =
    [
        "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BIF", "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        "CAD", "CDF", "CHF", "CNY", "COP", "CRC", "CUC", "CUP", "CVE", "CZK",
        "DKK", "DOP", "DZD",
        "EGP", "ERN", "ETB", "EUR",
        "FJD", "FKP",
        "GBP", "GEL", "GHS", "GIP", "GMD", "GTQ", "GYD",
        "HKD", "HNL", "HTG", "HUF",
        "IDR", "ILS", "INR", "IRR",
        "JMD",
        "KES", "KGS", "KHR", "KPW", "KYD", "KZT",
        "LAK", "LBP", "LKR", "LRD", "LSL",
        "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN",
        "NAD", "NGN", "NIO", "NOK", "NPR", "NZD",
        "PAB", "PEN", "PGK", "PHP", "PKR", "PLN",
        "QAR",
        "RON", "RSD", "RUB",
        "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL",
        "THB", "TJS", "TMT", "TOP", "TRY", "TTD", "TWD", "TZS",
        "UAH", "USD", "UYU", "UZS",
        "VES",
        "WST",
        "XCD",
        "YER",
        "ZAR", "ZMW", "ZWL",
    ];

    private static readonly IReadOnlyDictionary<string, int> MinorUnitDigitsByCode = BuildCatalog();

    /// <summary>
    /// Attempts to resolve the minor-unit (decimal-place) exponent for <paramref name="code"/>.
    /// </summary>
    /// <param name="code">The ISO 4217 alpha-3 currency code, uppercase (e.g. <c>"USD"</c>).</param>
    /// <param name="digits">
    /// When this method returns <see langword="true"/>, the minor-unit exponent for
    /// <paramref name="code"/>. Otherwise <c>0</c>.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="code"/> is a known currency code.</returns>
    public static bool TryGetMinorUnitDigits(string code, out int digits)
    {
        if (code is null)
        {
            digits = 0;
            return false;
        }

        return MinorUnitDigitsByCode.TryGetValue(code, out digits);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="code"/> is a known ISO 4217
    /// alpha-3 currency code in this catalog.
    /// </summary>
    public static bool IsKnownCode(string code) => code is not null && MinorUnitDigitsByCode.ContainsKey(code);

    private static IReadOnlyDictionary<string, int> BuildCatalog()
    {
        var catalog = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var code in TwoDecimalCodes)
            catalog[code] = DefaultMinorUnitDigits;

        foreach (var code in ZeroDecimalCodes)
            catalog[code] = ZeroMinorUnitDigits;

        foreach (var code in ThreeDecimalCodes)
            catalog[code] = ThreeMinorUnitDigits;

        return catalog;
    }
}
