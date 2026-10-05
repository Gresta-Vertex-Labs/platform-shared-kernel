namespace SharedKernel.Reporting.Pdf;

/// <summary>
/// The tabular PDF <see cref="IReportExporter{TRow}"/>, for injecting the PDF exporter directly. Registered for every
/// row type by <c>AddPdf</c>.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public interface IPdfReportExporter<TRow> : IReportExporter<TRow>;
