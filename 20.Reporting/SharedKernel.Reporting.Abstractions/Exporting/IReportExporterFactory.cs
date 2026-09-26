using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting;

/// <summary>Picks an <see cref="IReportExporter{TRow}"/> by format at runtime, e.g. from a <c>?format=xlsx</c> query.</summary>
/// <example>
/// <code>
/// Result&lt;ReportStreamOutcome&gt; result = await exporters.ParseFormat(format)          // "csv", "xlsx", ".pdf", "text/csv"
///     .Map(f => exporters.GetExporter&lt;Order&gt;(f))
///     .BindAsync(exporter => exporter.ExportToStreamAsync(rows, definition, response.Body, ct));
/// </code>
/// </example>
/// <remarks>
/// Registered by <c>AddSharedKernelReporting()</c>; each provider (<c>AddCsv</c>, <c>AddSpreadsheet</c>, <c>AddPdf</c>)
/// adds its format. Exporters are also resolvable directly as keyed services:
/// <c>[FromKeyedServices("xlsx")] IReportExporter&lt;Order&gt;</c>.
/// </remarks>
public interface IReportExporterFactory
{
    /// <summary>Gets the formats with a registered exporter.</summary>
    IReadOnlyCollection<ReportFormat> Formats { get; }

    /// <summary>
    /// Finds the registered format named by <paramref name="value"/>: a format name (<c>"xlsx"</c>), a file extension
    /// (<c>".xlsx"</c>) or a content type, ignoring case.
    /// </summary>
    /// <param name="value">The requested format, typically user input.</param>
    /// <returns>The format, or <see cref="ReportingErrorCodes.UnsupportedFormat"/> listing the supported ones.</returns>
    Result<ReportFormat> ParseFormat(string? value);

    /// <summary>Returns the exporter for <paramref name="format"/>.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="format">A registered format.</param>
    /// <returns>The exporter.</returns>
    /// <exception cref="InvalidOperationException">No exporter is registered for <paramref name="format"/>.</exception>
    IReportExporter<TRow> GetExporter<TRow>(ReportFormat format);
}
