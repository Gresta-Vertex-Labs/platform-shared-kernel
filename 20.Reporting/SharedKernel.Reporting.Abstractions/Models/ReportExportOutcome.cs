using SharedKernel.Storage;

namespace SharedKernel.Reporting.Abstractions.Models;

/// <summary>
/// The successful outcome of <see cref="Exporters.IReportExporter{TRow}.ExportAsync"/>.
/// </summary>
public sealed record ReportExportOutcome
{
    /// <summary>
    /// The durable handle to the stored exported object — its store, tenant and key — persist this, not
    /// <see cref="DownloadUrl"/>. Open it later with <c>IFileStorageFactory.Open(StoredFile)</c>.
    /// </summary>
    public required FileReference StoredFile { get; init; }

    /// <summary>
    /// A presigned download request (URL, HTTP method and expiry) for the exported object, populated only
    /// when the originating <see cref="ReportDestination.PresignedDownloadUrlExpiry"/> was
    /// non-<see langword="null"/>.
    /// </summary>
    public PresignedRequest? DownloadUrl { get; init; }

    /// <summary>
    /// The number of rows enumerated from the source during export, counted for free while
    /// streaming — no second pass over the row source is ever performed to compute this.
    /// </summary>
    public required long RowCount { get; init; }
}
