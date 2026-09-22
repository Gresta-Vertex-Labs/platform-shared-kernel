using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Models;

namespace SharedKernel.Reporting.Abstractions.Exporters;

/// <summary>
/// Provider-agnostic contract for streaming, memory-bounded generation of a structured tabular
/// report/data export in a specific format (CSV, spreadsheet, PDF, ...), delivered either through
/// object storage or directly to a caller-supplied <see cref="Stream"/>.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
/// <remarks>
/// <para>
/// <b>Invariant 1 — streaming, with no escape hatch.</b> Both members accept
/// <see cref="IAsyncEnumerable{T}"/> of <typeparamref name="TRow"/> — never
/// <see cref="IEnumerable{T}"/> or <see cref="List{T}"/>. No overload accepting a materialized
/// collection may ever be added to this contract: the moment one exists, every caller uses it and
/// the memory-boundedness guarantee this domain exists to provide is gone. A caller holding an
/// in-memory collection converts it via a one-line adapter (e.g. <c>MyList.ToAsyncEnumerable()</c>)
/// — that is the caller's problem to solve, never this contract's to weaken. This governs the
/// contract *shape* only — it is not a claim that every provider's own encoding step is itself
/// O(1)-memory; <c>SharedKernel.Reporting.Csv</c> genuinely is, while
/// <c>SharedKernel.Reporting.Spreadsheet</c>/<c>.Pdf</c> are not (their own XML docs state this in
/// capitals — a verified, permanent characteristic of the ClosedXML/MigraDoc-PdfSharp dependencies,
/// not a defect).
/// </para>
/// <para>
/// <b>Invariant 2 — delivery goes through storage, never through the response.</b>
/// <see cref="ExportAsync"/> is the primary path: output is written via
/// the named <c>SharedKernel.Storage</c> store <see cref="ReportDestination.Store"/> (resolved through
/// <c>IFileStorageFactory</c>), with an optional presigned download URL from the same store when
/// <see cref="ReportDestination.PresignedDownloadUrlExpiry"/> is set.
/// <see cref="ExportToStreamAsync"/> is a small-output/direct-stream convenience path — it still
/// accepts <see cref="IAsyncEnumerable{T}"/>, so it never reopens Invariant 1 — but it is never the
/// only way out: no code path may offer a fully-buffered byte array as the sole option.
/// </para>
/// <para>
/// <b>Invariant 3 — formatting is <see cref="System.Globalization.CultureInfo"/>, not translation.</b>
/// <see cref="ReportColumn{TRow}.Formatter"/> and <see cref="Formatting.ReportValueFormatting.Format"/>
/// format numbers, dates, and currency by culture — a BCL capability. This domain takes no
/// dependency on <c>SharedKernel.Localization</c> or any translation catalog. A column
/// <em>header</em> that needs translating is resolved by the caller before it reaches
/// <see cref="ReportColumn{TRow}.Header"/> — never inside this contract.
/// </para>
/// <para>
/// <b>Invariant 4/D-12 — scope boundaries, stated here as well as in the domain's own docs:</b>
/// tenant provisioning is <c>13.ServiceDefaults</c>'s concern, not this contract's; running an
/// export on a schedule composes in consumer code via <c>19.Scheduling</c>/<c>17.Workflows</c>, with
/// no layering grant needed either direction; no readiness probe exists here or is needed — this
/// domain is a stateless library holding no persistent connection to be ready or not ready.
/// </para>
/// <para>
/// <b>INVARIANT 5 — PII IS THE CALLER'S PROBLEM, AND IT IS SAID HERE, NOT ONLY IN A README.</b>
/// AN EXPORT IS A BULK EXTRACTION OF DATA TO A DURABLE FILE WITH A SHAREABLE URL — THE
/// HIGHEST-CONSEQUENCE PII SURFACE THIS PLATFORM HAS. THIS DOMAIN PERFORMS NO CLASSIFICATION OR
/// REDACTION OF ITS OWN. THAT IS <c>01.CORE/SHAREDKERNEL.DATAPRIVACY</c>'S JOB, APPLIED BY THE
/// CALLER BEFORE ROWS EVER REACH <see cref="ExportAsync"/>/<see cref="ExportToStreamAsync"/>. ROWS
/// ARRIVE ALREADY-REDACTED OR THEY LEAVE UN-REDACTED — THERE IS NO SAFETY NET HERE.
/// </para>
/// </remarks>
public interface IReportExporter<TRow>
{
    /// <summary>
    /// Streams <paramref name="rows"/> through this exporter's format and delivers the encoded
    /// output to <paramref name="destination"/>'s store via <c>IFileStorage</c>.
    /// </summary>
    /// <param name="rows">
    /// The row source. Enumerated exactly once, forward-only. See this interface's Invariant 1 —
    /// never pass a materializing adapter that defeats streaming upstream of this call.
    /// </param>
    /// <param name="definition">
    /// The column/culture/title definition describing how <paramref name="rows"/> renders.
    /// </param>
    /// <param name="destination">Where the encoded output is written.</param>
    /// <param name="cancellationToken">
    /// Token observed both while enumerating <paramref name="rows"/> and while the encoded bytes
    /// upload — cancelling stops consuming the row source promptly rather than draining it.
    /// </param>
    /// <returns>
    /// The <see cref="ReportExportOutcome"/> on success — including the row count, counted for free
    /// while streaming — or a failed <see cref="Result{T}"/> via <see cref="Errors.ReportingErrors"/>
    /// (definition/destination validation) or <c>StorageErrors</c> (a genuine storage-layer failure).
    /// </returns>
    Task<Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams <paramref name="rows"/> through this exporter's format directly into
    /// <paramref name="destination"/> — the Invariant-2-sanctioned small-output/direct-stream
    /// convenience path. Still accepts <see cref="IAsyncEnumerable{T}"/>, never
    /// <see cref="IEnumerable{T}"/>/<see cref="List{T}"/> — see this interface's Invariant 1.
    /// </summary>
    /// <param name="rows">The row source. Enumerated exactly once, forward-only.</param>
    /// <param name="definition">
    /// The column/culture/title definition describing how <paramref name="rows"/> renders.
    /// </param>
    /// <param name="destination">
    /// The caller-owned stream to write encoded output to. Never disposed by this method.
    /// </param>
    /// <param name="cancellationToken">
    /// Token observed while enumerating <paramref name="rows"/> — cancelling stops consuming the
    /// row source promptly rather than draining it.
    /// </param>
    /// <returns>
    /// A successful <see cref="Result"/> once the output has been fully written to
    /// <paramref name="destination"/>, or a failed result via <see cref="Errors.ReportingErrors"/>.
    /// </returns>
    Task<Result> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken);
}
