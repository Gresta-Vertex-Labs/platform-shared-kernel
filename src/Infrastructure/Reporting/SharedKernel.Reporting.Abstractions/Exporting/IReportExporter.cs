using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting;

/// <summary>
/// Streams rows into a report of one <see cref="Format"/> (CSV, Excel, PDF, …), delivered to object storage or to a
/// stream.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
/// <example>
/// <code>
/// // Injected per format (ICsvReportExporter&lt;T&gt;, ISpreadsheetReportExporter&lt;T&gt;, IPdfReportExporter&lt;T&gt;),
/// // or picked at runtime through IReportExporterFactory.
/// Result&lt;ReportExportOutcome&gt; stored = await exporter.ExportAsync(
///     repository.StreamAsync(spec, ct),        // IAsyncEnumerable&lt;Order&gt; — never a List
///     definition,
///     new ReportDestination { Store = "reports", Key = "orders/2026-09.xlsx", PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15) },
///     ct);
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Streaming only.</b> Rows are an <see cref="IAsyncEnumerable{T}"/>, enumerated once, forward-only; there is
/// deliberately no overload taking a list, so an export never needs the whole result set in memory. CSV and Excel
/// encode in constant memory; PDF builds its document in memory and is capped by its <c>MaxRows</c> option.
/// </para>
/// <para>
/// <b>Delivery.</b> <see cref="ExportAsync"/> uploads the bytes to storage while they are produced — nothing is
/// buffered in between — and can return a presigned download link. <see cref="ExportToStreamAsync"/> writes to any
/// stream, such as an HTTP response body.
/// </para>
/// <para>
/// <b>Personal data.</b> An export is a bulk copy of data into a durable, shareable file. This library classifies and
/// redacts nothing: redact with <c>SharedKernel.DataPrivacy</c> before the rows reach the exporter. Spans, metrics and
/// logs never carry row content, keys or file names.
/// </para>
/// <para>
/// <b>Failures.</b> Expected failures are a failed <see cref="Result"/>: an invalid definition or destination
/// (<see cref="ReportingErrorCodes"/>), a row limit, or a storage failure (its <c>storage.*</c> code). An exception
/// thrown by the row source, a value function or a formatter propagates — it is a bug, not an outcome.
/// </para>
/// </remarks>
public interface IReportExporter<TRow>
{
    /// <summary>Gets the format this exporter writes.</summary>
    ReportFormat Format { get; }

    /// <summary>Streams <paramref name="rows"/> into a report and stores it at <paramref name="destination"/>.</summary>
    /// <param name="rows">The rows, enumerated once.</param>
    /// <param name="definition">The columns, culture and title.</param>
    /// <param name="destination">The store and key to write to.</param>
    /// <param name="cancellationToken">Stops enumerating the rows and the upload.</param>
    /// <returns>The stored report, or a failure.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Storage is not registered, the store is unknown, or the tenant does not match the store.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken = default);

    /// <summary>Streams <paramref name="rows"/> into a report written to <paramref name="destination"/>.</summary>
    /// <param name="rows">The rows, enumerated once.</param>
    /// <param name="definition">The columns, culture and title.</param>
    /// <param name="destination">The stream to write to; left open. On a failure it may hold part of the report.</param>
    /// <param name="cancellationToken">Stops enumerating the rows.</param>
    /// <returns>The row count and size, or a failure.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<ReportStreamOutcome>> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken = default);
}
