using System.Globalization;

namespace SharedKernel.Reporting.Abstractions.Formatting;

/// <summary>
/// The default value-to-text formatting rule every provider applies when a
/// <see cref="Models.ReportColumn{TRow}"/> supplies no <see cref="Models.ReportColumn{TRow}.Formatter"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is BCL <see cref="CultureInfo"/> formatting — numbers, dates, and currency values format
/// correctly per culture because <see cref="IFormattable"/> does that natively. This domain takes no
/// dependency on any translation catalog to achieve it; see
/// <see cref="Exporters.IReportExporter{TRow}"/>'s type-level documentation for that boundary.
/// </para>
/// <para>
/// Living in <c>.Abstractions</c> means all three providers (<c>.Csv</c>, <c>.Spreadsheet</c>,
/// <c>.Pdf</c>) apply the identical default rule rather than three subtly different ones.
/// </para>
/// </remarks>
public static class ReportValueFormatting
{
    /// <summary>
    /// Formats <paramref name="value"/> for display under <paramref name="culture"/>.
    /// </summary>
    /// <param name="value">The raw value to format. <see langword="null"/> means "blank cell".</param>
    /// <param name="culture">The culture to format numbers, dates, and currency values under.</param>
    /// <returns>
    /// <see langword="null"/> when <paramref name="value"/> is <see langword="null"/> (meaning
    /// "blank cell" — never the literal string <c>"null"</c>); <c>"True"</c>/<c>"False"</c> for a
    /// <see cref="bool"/>; the culture-aware <see cref="IFormattable.ToString(string?, IFormatProvider?)"/>
    /// result for any other <see cref="IFormattable"/> value (numbers, dates, currency); otherwise
    /// <paramref name="value"/>'s own <see cref="object.ToString"/>.
    /// </returns>
    public static string? Format(object? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return value switch
        {
            null => null,
            bool booleanValue => booleanValue ? "True" : "False",
            IFormattable formattable => formattable.ToString(null, culture),
            _ => value.ToString(),
        };
    }
}
