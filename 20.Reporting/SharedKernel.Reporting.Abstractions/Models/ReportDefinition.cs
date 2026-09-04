using System.Globalization;

namespace SharedKernel.Reporting.Abstractions.Models;

/// <summary>
/// Describes a complete report shape: its ordered set of <see cref="ReportColumn{TRow}"/>s, the
/// <see cref="CultureInfo"/> used for default value formatting, and an optional title a provider may
/// use as a sheet name or document heading.
/// </summary>
/// <typeparam name="TRow">The row type this definition exports.</typeparam>
/// <remarks>
/// This type carries no data-classification or PII-redaction semantics — see
/// <see cref="Exporters.IReportExporter{TRow}"/>'s type-level documentation for that boundary.
/// </remarks>
public sealed record ReportDefinition<TRow>
{
    /// <summary>
    /// The report's columns, in any declaration order — every provider renders them ordered by
    /// <see cref="ReportColumn{TRow}.Ordinal"/>, not by this list's own order. Must contain at
    /// least one column.
    /// </summary>
    public required IReadOnlyList<ReportColumn<TRow>> Columns { get; init; }

    /// <summary>
    /// The culture used by <see cref="Formatting.ReportValueFormatting.Format"/> and passed to every
    /// <see cref="ReportColumn{TRow}.Formatter"/>. Defaults to <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    /// <remarks>
    /// This is a BCL number/date/currency formatting culture — never a translation-catalog lookup.
    /// See <see cref="Exporters.IReportExporter{TRow}"/>'s type-level documentation for the
    /// formatting-versus-translation boundary this domain deliberately does not cross.
    /// </remarks>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// An optional title a provider may use as a worksheet name or document heading. Never
    /// mandatory — a provider that has no natural place for a title (e.g. plain CSV) ignores it.
    /// </summary>
    public string? Title { get; init; }
}
