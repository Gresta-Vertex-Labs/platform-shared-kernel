using SharedKernel.Reporting.Abstractions.Exporters;

namespace SharedKernel.Reporting.Spreadsheet.Exporters;

/// <summary>
/// Provider-exclusive marker interface for the ClosedXML-backed spreadsheet
/// <see cref="IReportExporter{TRow}"/> implementation. Declared only in this package — mirroring
/// <c>09.Search</c>/<c>10.Intelligence</c>'s established compile-time provider-exclusivity pattern.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
public interface ISpreadsheetReportExporter<TRow> : IReportExporter<TRow>;
