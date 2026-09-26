namespace SharedKernel.Reporting.Csv;

/// <summary>
/// The CSV <see cref="IReportExporter{TRow}"/>, for injecting the CSV exporter directly. Registered for every row type
/// by <c>AddCsv</c>.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public interface ICsvReportExporter<TRow> : IReportExporter<TRow>;
