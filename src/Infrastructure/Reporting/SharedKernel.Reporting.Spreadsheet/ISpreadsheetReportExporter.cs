namespace SharedKernel.Reporting.Spreadsheet;

/// <summary>
/// The Excel (.xlsx) <see cref="IReportExporter{TRow}"/>, for injecting the spreadsheet exporter directly. Registered
/// for every row type by <c>AddSpreadsheet</c>.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public interface ISpreadsheetReportExporter<TRow> : IReportExporter<TRow>;
