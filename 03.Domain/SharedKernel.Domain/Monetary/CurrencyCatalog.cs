using System.Collections.Frozen;

namespace SharedKernel.Domain.Monetary;

/// <summary>
/// The ISO 4217 currencies this package accepts, with the number of decimal places of each minor unit.
/// </summary>
/// <remarks>
/// <para>
/// Covers every active national and supranational transactional currency as of <see cref="RegistryAsOf"/>.
/// Fund codes (such as <c>USN</c> and <c>CLF</c>), special drawing rights (<c>XDR</c>) and precious metals
/// (<c>XAU</c>) are deliberately excluded, since they are not amounts a service charges or pays.
/// </para>
/// <para>
/// The table is compiled in. When ISO publishes an amendment, a new package version updates it; nothing is
/// loaded at runtime.
/// </para>
/// </remarks>
public static class CurrencyCatalog
{
    /// <summary>The ISO 4217 amendment state this table reflects, as a year and month.</summary>
    public const string RegistryAsOf = "2026-09";

    private static readonly string[] ZeroDecimalCodes =
    [
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
    ];

    private static readonly string[] ThreeDecimalCodes =
    [
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND",
    ];

    private static readonly string[] TwoDecimalCodes =
    [
        "AED", "AFN", "ALL", "AMD", "AOA", "ARS", "AUD", "AWG", "AZN",
        "BAM", "BBD", "BDT", "BGN", "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD",
        "CAD", "CDF", "CHF", "CNY", "COP", "CRC", "CUP", "CVE", "CZK",
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
        "VED", "VES",
        "WST",
        "XCD", "XCG",
        "YER",
        "ZAR", "ZMW", "ZWG",
    ];

    private static readonly FrozenDictionary<string, int> MinorUnitDigitsByCode = BuildCatalog();

    /// <summary>Gets every code in the catalog.</summary>
    public static IReadOnlyCollection<string> Codes => MinorUnitDigitsByCode.Keys;

    /// <summary>Looks up the number of minor-unit decimal places for <paramref name="code"/>.</summary>
    /// <param name="code">An uppercase ISO 4217 alphabetic code, e.g. <c>USD</c>.</param>
    /// <param name="digits">The number of decimal places when the code is known; otherwise <c>0</c>.</param>
    /// <returns><see langword="true"/> when the code is in the catalog.</returns>
    public static bool TryGetMinorUnitDigits(string? code, out int digits)
    {
        if (code is not null && MinorUnitDigitsByCode.TryGetValue(code, out digits))
            return true;

        digits = 0;
        return false;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="code"/> is in the catalog.</summary>
    /// <param name="code">An uppercase ISO 4217 alphabetic code, e.g. <c>USD</c>.</param>
    /// <returns><see langword="true"/> when the code is known.</returns>
    public static bool IsKnownCode(string? code) => code is not null && MinorUnitDigitsByCode.ContainsKey(code);

    private static FrozenDictionary<string, int> BuildCatalog()
    {
        var catalog = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in TwoDecimalCodes)
            catalog.Add(code, 2);
        foreach (var code in ZeroDecimalCodes)
            catalog.Add(code, 0);
        foreach (var code in ThreeDecimalCodes)
            catalog.Add(code, 3);
        return catalog.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
