using System.Collections.Frozen;

namespace SharedKernel.Validation.Internal;

/// <summary>
/// The SWIFT IBAN registry (ISO 13616): for each country, the total IBAN length and the structure
/// of the BBAN, the national account number that follows the country code and check digits.
/// </summary>
/// <remarks>
/// A BBAN format lists segments of <c>n</c> digits, <c>a</c> upper-case letters and <c>c</c>
/// letters or digits, as the registry publishes them: <c>"4a,14n"</c> is four letters then
/// fourteen digits. Generated from registry release 99 (December 2024), 89 countries; each
/// format's segments add up to the total length minus four.
/// </remarks>
internal static class IbanRegistry
{
    public const string ReleaseName = "SWIFT IBAN Registry release 99 (December 2024)";

    public static readonly FrozenDictionary<string, IbanCountryFormat> Countries = Build(
    [
        ("AD", 24, "8n,12c"), // Andorra
        ("AE", 23, "3n,16n"), // United Arab Emirates
        ("AL", 28, "8n,16c"), // Albania
        ("AT", 20, "16n"), // Austria
        ("AZ", 28, "4a,20c"), // Azerbaijan
        ("BA", 20, "16n"), // Bosnia and Herzegovina
        ("BE", 16, "12n"), // Belgium
        ("BG", 22, "4a,6n,8c"), // Bulgaria
        ("BH", 22, "4a,14c"), // Bahrain
        ("BI", 27, "5n,5n,11n,2n"), // Burundi
        ("BR", 29, "23n,1a,1c"), // Brazil
        ("BY", 28, "4c,4n,16c"), // Belarus
        ("CH", 21, "5n,12c"), // Switzerland
        ("CR", 22, "18n"), // Costa Rica
        ("CY", 28, "8n,16c"), // Cyprus
        ("CZ", 24, "20n"), // Czech Republic
        ("DE", 22, "18n"), // Germany
        ("DJ", 27, "5n,5n,11n,2n"), // Djibouti
        ("DK", 18, "14n"), // Denmark
        ("DO", 28, "4c,20n"), // Dominican Republic
        ("EE", 20, "16n"), // Estonia
        ("EG", 29, "25n"), // Egypt
        ("ES", 24, "20n"), // Spain
        ("FI", 18, "14n"), // Finland
        ("FK", 18, "2a,12n"), // Falkland Islands
        ("FO", 18, "14n"), // Faroe Islands
        ("FR", 27, "10n,11c,2n"), // France
        ("GB", 22, "4a,14n"), // United Kingdom
        ("GE", 22, "2a,16n"), // Georgia (country)|Georgia
        ("GI", 23, "4a,15c"), // Gibraltar
        ("GL", 18, "14n"), // Greenland
        ("GR", 27, "7n,16c"), // Greece
        ("GT", 28, "4c,20c"), // Guatemala
        ("HN", 28, "4a,20n"), // Honduras
        ("HR", 21, "17n"), // Croatia
        ("HU", 28, "24n"), // Hungary
        ("IE", 22, "4a,6n,8n"), // Republic of Ireland|Ireland
        ("IL", 23, "19n"), // Israel
        ("IQ", 23, "4a,15n"), // Iraq
        ("IS", 26, "22n"), // Iceland
        ("IT", 27, "1a,10n,12c"), // Italy
        ("JO", 30, "4a,4n,18c"), // Jordan
        ("KW", 30, "4a,22c"), // Kuwait
        ("KZ", 20, "3n,13c"), // Kazakhstan
        ("LB", 28, "4n,20c"), // Lebanon
        ("LC", 32, "4a,24c"), // Saint Lucia
        ("LI", 21, "5n,12c"), // Liechtenstein
        ("LT", 20, "16n"), // Lithuania
        ("LU", 20, "3n,13c"), // Luxembourg
        ("LV", 21, "4a,13c"), // Latvia
        ("LY", 25, "21n"), // Libya
        ("MC", 27, "10n,11c,2n"), // Monaco
        ("MD", 24, "2c,18c"), // Moldova
        ("ME", 22, "18n"), // Montenegro
        ("MK", 19, "3n,10c,2n"), // North Macedonia
        ("MN", 20, "4n,12n"), // Mongolia
        ("MR", 27, "23n"), // Mauritania
        ("MT", 31, "4a,5n,18c"), // Malta
        ("MU", 30, "4a,19n,3a"), // Mauritius
        ("NI", 28, "4a,20n"), // Nicaragua
        ("NL", 18, "4a,10n"), // Netherlands
        ("NO", 15, "11n"), // Norway
        ("OM", 23, "3n,16c"), // Oman
        ("PK", 24, "4a,16c"), // Pakistan
        ("PL", 28, "24n"), // Poland
        ("PS", 29, "4a,21c"), // Palestinian territories
        ("PT", 25, "21n"), // Portugal
        ("QA", 29, "4a,21c"), // Qatar
        ("RO", 24, "4a,16c"), // Romania
        ("RS", 22, "18n"), // Serbia
        ("RU", 33, "14n,15c"), // Russia
        ("SA", 24, "2n,18c"), // Saudi Arabia
        ("SC", 31, "4a,20n,3a"), // Seychelles
        ("SD", 18, "14n"), // Sudan
        ("SE", 24, "20n"), // Sweden
        ("SI", 19, "15n"), // Slovenia
        ("SK", 24, "20n"), // Slovakia
        ("SM", 27, "1a,10n,12c"), // San Marino
        ("SO", 23, "4n,3n,12n"), // Somalia
        ("ST", 25, "21n"), // São Tomé and Príncipe
        ("SV", 28, "4a,20n"), // El Salvador
        ("TL", 23, "19n"), // East Timor
        ("TN", 24, "20n"), // Tunisia
        ("TR", 26, "5n,1n,16c"), // Turkey
        ("UA", 29, "6n,19c"), // Ukraine
        ("VA", 22, "3n,15n"), // Vatican City
        ("VG", 24, "4a,16n"), // British Virgin Islands|Virgin Islands, British
        ("XK", 20, "4n,10n,2n"), // Kosovo
        ("YE", 30, "4a,4n,18c"), // Yemen
    ]);

    private static FrozenDictionary<string, IbanCountryFormat> Build((string Country, int Length, string Bban)[] rows) =>
        rows.ToFrozenDictionary(row => row.Country, row => new IbanCountryFormat(row.Length, ExpandMask(row.Bban)), StringComparer.Ordinal);

    // "4a,14n" → "aaaannnnnnnnnnnnnn": one class character per BBAN position.
    private static string ExpandMask(string format)
    {
        var mask = new System.Text.StringBuilder(30);
        foreach (string segment in format.Split(','))
        {
            mask.Append(segment[^1], int.Parse(segment[..^1], System.Globalization.CultureInfo.InvariantCulture));
        }

        return mask.ToString();
    }
}

/// <summary>One registry entry: the total IBAN length and a per-position BBAN character-class mask.</summary>
internal sealed record IbanCountryFormat(int Length, string BbanMask)
{
    /// <summary>Whether <paramref name="bban"/> matches the mask: <c>n</c> a digit, <c>a</c> an upper-case letter, <c>c</c> either.</summary>
    public bool Matches(ReadOnlySpan<char> bban)
    {
        if (bban.Length != BbanMask.Length)
        {
            return false;
        }

        for (int i = 0; i < bban.Length; i++)
        {
            char c = bban[i];
            bool ok = BbanMask[i] switch
            {
                'n' => char.IsAsciiDigit(c),
                'a' => char.IsAsciiLetterUpper(c),
                _ => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c),
            };

            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
