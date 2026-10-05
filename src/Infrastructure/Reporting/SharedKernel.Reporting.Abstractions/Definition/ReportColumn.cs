using System.Globalization;

namespace SharedKernel.Reporting;

/// <summary>
/// One output column of a report: its header, how it reads a value from a row, and how that value is shown.
/// </summary>
/// <typeparam name="TRow">The row type of the report.</typeparam>
/// <remarks>
/// <para>
/// Usually built with <see cref="ReportDefinition.For{TRow}"/>. Columns render in the order they appear in
/// <see cref="ReportDefinition{TRow}.Columns"/>.
/// </para>
/// <para>
/// <b>How a value is shown.</b> A <see langword="null"/> value is an empty cell, never the text <c>"null"</c>.
/// With a <see cref="Formatter"/>, the cell is exactly the text it returns. Otherwise text formats (CSV, PDF) apply
/// <see cref="Format"/> and the report's culture through <see cref="ReportValueFormatting.Format(object?, CultureInfo, string?)"/>,
/// and the spreadsheet format writes numbers, dates, times and booleans as real typed cells — sortable and
/// summable in Excel — translating <see cref="Format"/> to an Excel number format where it can.
/// </para>
/// </remarks>
public sealed record ReportColumn<TRow>
{
    /// <summary>Gets the header text. Translate it before building the definition; this library does not.</summary>
    public required string Header { get; init; }

    /// <summary>Gets the function reading this column's value from a row. <see langword="null"/> is an empty cell.</summary>
    public required Func<TRow, object?> Value { get; init; }

    /// <summary>
    /// Gets an optional .NET format string for the value, e.g. <c>"N2"</c>, <c>"P1"</c> or <c>"yyyy-MM-dd"</c>.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Gets an optional function turning the value into the cell's text, for formatting a format string cannot
    /// express. It receives the value and the report's culture; <see langword="null"/> is an empty cell. A column with
    /// a formatter is always written as text, also in a spreadsheet.
    /// </summary>
    public Func<object?, CultureInfo, string?>? Formatter { get; init; }

    /// <summary>
    /// Gets the horizontal alignment. <see cref="ReportColumnAlignment.Auto"/> (the default) right-aligns numbers and
    /// left-aligns everything else.
    /// </summary>
    public ReportColumnAlignment Alignment { get; init; }

    /// <summary>
    /// Gets the column's width relative to the other columns, <c>1</c> by default. A column with <c>2</c> is twice as
    /// wide as one with <c>1</c>. Used by the PDF and spreadsheet formats; CSV has no widths.
    /// </summary>
    public double RelativeWidth { get; init; } = 1d;
}
