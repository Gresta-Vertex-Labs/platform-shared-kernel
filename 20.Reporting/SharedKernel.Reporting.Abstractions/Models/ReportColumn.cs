using System.Globalization;

namespace SharedKernel.Reporting.Abstractions.Models;

/// <summary>
/// Describes one output column of a report: where it reads its value from a <typeparamref name="TRow"/>
/// instance, what header text it renders, and (optionally) how it formats that value to text.
/// </summary>
/// <typeparam name="TRow">The row type the owning <see cref="ReportDefinition{TRow}"/> exports.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Ordinal"/> is an explicit output position, not the array/list index the column happens
/// to occupy in <see cref="ReportDefinition{TRow}.Columns"/> — a caller may declare columns in any
/// order and every provider renders them sorted by <see cref="Ordinal"/>.
/// </para>
/// <para>
/// <b>Blank-cell rule:</b> when <see cref="ValueSelector"/> returns <see langword="null"/>, or when
/// <see cref="Formatter"/> is supplied and returns <see langword="null"/>, every provider writes a
/// blank cell/field — never the literal string <c>"null"</c>.
/// </para>
/// </remarks>
public sealed record ReportColumn<TRow>
{
    /// <summary>The column's header text.</summary>
    public required string Header { get; init; }

    /// <summary>
    /// The column's explicit output position among its siblings in the same
    /// <see cref="ReportDefinition{TRow}.Columns"/> list. Every provider orders columns by this
    /// value, not by list order.
    /// </summary>
    public required int Ordinal { get; init; }

    /// <summary>Reads this column's raw value from a <typeparamref name="TRow"/> instance.</summary>
    /// <remarks>
    /// A <see langword="null"/> return value means "blank cell" — no provider ever writes the
    /// literal string <c>"null"</c>.
    /// </remarks>
    public required Func<TRow, object?> ValueSelector { get; init; }

    /// <summary>
    /// An optional column-specific formatter converting the raw value produced by
    /// <see cref="ValueSelector"/>, plus the exporting <see cref="ReportDefinition{TRow}.Culture"/>,
    /// to display text. When <see langword="null"/>, every provider falls back to
    /// <see cref="Formatting.ReportValueFormatting.Format"/>.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> return value means "blank cell", identically to a
    /// <see langword="null"/> <see cref="ValueSelector"/> result.
    /// </remarks>
    public Func<object?, CultureInfo, string?>? Formatter { get; init; }
}
