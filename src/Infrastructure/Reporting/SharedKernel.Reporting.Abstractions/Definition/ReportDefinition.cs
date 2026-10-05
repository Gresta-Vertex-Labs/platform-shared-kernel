using System.Globalization;

namespace SharedKernel.Reporting;

/// <summary>
/// The shape of a report: its columns, the culture values are formatted in, and an optional title.
/// </summary>
/// <typeparam name="TRow">The row type of the report.</typeparam>
/// <example>
/// <code>
/// ReportDefinition&lt;Order&gt; definition = ReportDefinition.For&lt;Order&gt;()
///     .Title("Orders — September")
///     .Culture(CultureInfo.GetCultureInfo("tr-TR"))
///     .Column("Order", o => o.Number)
///     .Column("Placed", o => o.PlacedAt, format: "yyyy-MM-dd")
///     .Column("Customer", o => o.CustomerName, relativeWidth: 2)
///     .Column("Total", o => o.Total, format: "N2")
///     .Build();
/// </code>
/// </example>
/// <remarks>
/// A definition carries no data classification: rows reach an exporter already redacted, or they leave unredacted.
/// Apply <c>SharedKernel.DataPrivacy</c> in the query that produces the rows.
/// </remarks>
public sealed record ReportDefinition<TRow>
{
    /// <summary>Gets the columns, in output order. At least one is required.</summary>
    public required IReadOnlyList<ReportColumn<TRow>> Columns { get; init; }

    /// <summary>
    /// Gets the culture numbers, dates and currencies are formatted in (not a translation catalog). Defaults to
    /// <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Gets an optional title: the heading and document title of a PDF, the sheet name and document title of a
    /// workbook. CSV has no place for it and ignores it.
    /// </summary>
    public string? Title { get; init; }
}

/// <summary>Starts a <see cref="ReportDefinition{TRow}"/>.</summary>
public static class ReportDefinition
{
    /// <summary>Starts building the definition of a report over <typeparamref name="TRow"/>.</summary>
    /// <typeparam name="TRow">The row type of the report.</typeparam>
    /// <returns>A builder; add columns, then call <see cref="ReportDefinitionBuilder{TRow}.Build"/>.</returns>
    public static ReportDefinitionBuilder<TRow> For<TRow>() => new();
}
