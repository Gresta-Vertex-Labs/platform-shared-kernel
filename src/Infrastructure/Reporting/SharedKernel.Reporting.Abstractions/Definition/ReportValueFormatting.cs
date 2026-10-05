using System.Globalization;

namespace SharedKernel.Reporting;

/// <summary>
/// The value-to-text rule every text format (CSV, PDF) applies to a column without a
/// <see cref="ReportColumn{TRow}.Formatter"/>. One rule, so every provider shows a value the same way.
/// </summary>
/// <remarks>
/// This is <see cref="CultureInfo"/> formatting of numbers, dates and currencies — not translation.
/// </remarks>
public static class ReportValueFormatting
{
    /// <summary>Formats <paramref name="value"/> under <paramref name="culture"/>.</summary>
    /// <param name="value">The value; <see langword="null"/> is an empty cell.</param>
    /// <param name="culture">The culture numbers, dates and currencies are formatted in.</param>
    /// <param name="format">An optional .NET format string, applied to <see cref="IFormattable"/> values.</param>
    /// <returns>
    /// <see langword="null"/> for a <see langword="null"/> value; <c>"True"</c>/<c>"False"</c> for a
    /// <see cref="bool"/>; the culture-aware <see cref="IFormattable.ToString(string?, IFormatProvider?)"/> for numbers,
    /// dates and other formattable values; otherwise <see cref="object.ToString"/>.
    /// </returns>
    /// <exception cref="FormatException"><paramref name="format"/> is not valid for the value's type.</exception>
    public static string? Format(object? value, CultureInfo culture, string? format = null)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return value switch
        {
            null => null,
            string text => text,
            bool boolean => boolean ? bool.TrueString : bool.FalseString,
            IFormattable formattable => formattable.ToString(format, culture),
            _ => value.ToString(),
        };
    }

    /// <summary>Formats a column's value from a row, honouring its <see cref="ReportColumn{TRow}.Formatter"/> and <see cref="ReportColumn{TRow}.Format"/>.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="column">The column.</param>
    /// <param name="value">The value the column read from the row.</param>
    /// <param name="culture">The report's culture.</param>
    /// <returns>The cell text, or <see langword="null"/> for an empty cell.</returns>
    public static string? FormatColumnValue<TRow>(ReportColumn<TRow> column, object? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.Formatter is { } formatter ? formatter(value, culture) : Format(value, culture, column.Format);
    }

    /// <summary>Returns whether <paramref name="value"/> is a number (an integral or floating-point primitive, or <see cref="decimal"/>).</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> for a numeric value.</returns>
    public static bool IsNumber(object? value) => value is sbyte or byte or short or ushort or int or uint or long or ulong
        or float or double or decimal or Int128 or UInt128 or Half;
}
