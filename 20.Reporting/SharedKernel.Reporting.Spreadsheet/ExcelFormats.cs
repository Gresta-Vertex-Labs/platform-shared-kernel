using System.Globalization;
using System.Text;

namespace SharedKernel.Reporting.Spreadsheet;

/// <summary>Excel sheet-name rules and the translation of .NET format strings into Excel number formats.</summary>
internal static class ExcelFormats
{
    public const string DateTime = "yyyy-mm-dd hh:mm:ss";
    public const string Date = "yyyy-mm-dd";
    public const string Time = "hh:mm:ss";
    public const string Duration = "[h]:mm:ss";

    private const int MaxSheetNameLength = 31;

    public static readonly char[] InvalidSheetNameChars = ['\\', '/', '?', '*', '[', ']', ':'];

    /// <summary>A valid worksheet name from a report title, or <paramref name="fallback"/>.</summary>
    public static string SheetName(string? title, string fallback)
    {
        var name = new StringBuilder(title?.Length ?? 0);
        foreach (char c in title ?? string.Empty)
        {
            name.Append(Array.IndexOf(InvalidSheetNameChars, c) >= 0 || char.IsControl(c) ? '_' : c);
        }

        string sanitized = name.ToString().Trim().Trim('\'').Trim();
        if (sanitized.Length == 0)
        {
            sanitized = fallback;
        }

        return sanitized.Length > MaxSheetNameLength ? sanitized[..MaxSheetNameLength] : sanitized;
    }

    /// <summary>
    /// The Excel number format for a number column with the .NET <paramref name="format"/>, or <see langword="null"/>
    /// for General. Standard formats (N, F, D, P, E, C with a precision) are translated; a custom format such as
    /// <c>#,##0.00</c> is passed through, since Excel shares that syntax.
    /// </summary>
    public static string? Number(string? format, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        if (!IsStandard(format, out char specifier, out int? precision))
        {
            return format;
        }

        return char.ToUpperInvariant(specifier) switch
        {
            'N' => "#,##0" + Decimals(precision ?? 2),
            'F' => "0" + Decimals(precision ?? 2),
            'D' => precision is > 1 ? new string('0', precision.Value) : "0",
            'P' => "0" + Decimals(precision ?? 2) + "%",
            'E' => "0" + Decimals(precision ?? 6) + "E+00",
            'C' => Currency(culture, precision ?? culture.NumberFormat.CurrencyDecimalDigits),
            _ => null,
        };
    }

    /// <summary>
    /// The Excel date/time format for a column with the .NET <paramref name="format"/>, or <paramref name="fallback"/>
    /// when it has none. Standard date formats resolve through <paramref name="culture"/>'s patterns.
    /// </summary>
    public static string DateAndTime(string? format, CultureInfo culture, string fallback)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return fallback;
        }

        DateTimeFormatInfo patterns = culture.DateTimeFormat;
        string? custom = format.Length != 1
            ? format
            : format[0] switch
            {
                'd' => patterns.ShortDatePattern,
                'D' => patterns.LongDatePattern,
                't' => patterns.ShortTimePattern,
                'T' => patterns.LongTimePattern,
                'g' => patterns.ShortDatePattern + " " + patterns.ShortTimePattern,
                'G' => patterns.ShortDatePattern + " " + patterns.LongTimePattern,
                'f' => patterns.LongDatePattern + " " + patterns.ShortTimePattern,
                'F' => patterns.FullDateTimePattern,
                's' or 'o' or 'O' or 'u' => "yyyy-MM-ddTHH:mm:ss",
                _ => null,
            };

        return custom is null ? fallback : TranslateDatePattern(custom);
    }

    /// <summary>Translates a .NET custom date/time pattern into Excel's syntax.</summary>
    internal static string TranslateDatePattern(string pattern)
    {
        var excel = new StringBuilder(pattern.Length + 8);
        for (var i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            int run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c)
            {
                run++;
            }

            switch (c)
            {
                case 'y' or 'd' or 'h' or 'm' or 's':
                    excel.Append(c, run);
                    break;
                case 'M':
                    excel.Append('m', run);
                    break;
                case 'H':
                    excel.Append('h', run);
                    break;
                case 'f' or 'F':
                    excel.Append('0', run);
                    break;
                case 't':
                    excel.Append("AM/PM");
                    break;
                case 'z' or 'K' or 'g':
                    break;
                case '\'' or '"':
                    int end = pattern.IndexOf(c, i + 1);
                    string literal = end < 0 ? pattern[(i + 1)..] : pattern[(i + 1)..end];
                    excel.Append('"').Append(literal.Replace("\"", string.Empty, StringComparison.Ordinal)).Append('"');
                    i = end < 0 ? pattern.Length : end;
                    continue;
                case '\\' when i + 1 < pattern.Length:
                    excel.Append('\\').Append(pattern[i + 1]);
                    i++;
                    continue;
                case ' ' or ':' or '/' or '-' or '.' or ',' or '(' or ')':
                    excel.Append(c, run);
                    break;
                default:
                    excel.Append('"').Append(c, run).Append('"');
                    break;
            }

            i += run - 1;
        }

        return excel.ToString();
    }

    private static bool IsStandard(string format, out char specifier, out int? precision)
    {
        specifier = format[0];
        precision = null;
        if (!char.IsAsciiLetter(specifier) || format.Length > 3)
        {
            return false;
        }

        if (format.Length == 1)
        {
            return true;
        }

        if (int.TryParse(format.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int digits))
        {
            precision = digits;
            return true;
        }

        return false;
    }

    private static string Decimals(int precision) => precision <= 0 ? string.Empty : "." + new string('0', Math.Min(precision, 30));

    private static string Currency(CultureInfo culture, int precision)
    {
        NumberFormatInfo numbers = culture.NumberFormat;
        string amount = "#,##0" + Decimals(precision);
        string symbol = "\"" + numbers.CurrencySymbol.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"";

        // CurrencyPositivePattern: 0 "$n", 1 "n$", 2 "$ n", 3 "n $".
        return numbers.CurrencyPositivePattern switch
        {
            0 => symbol + amount,
            1 => amount + symbol,
            2 => symbol + " " + amount,
            _ => amount + " " + symbol,
        };
    }
}
