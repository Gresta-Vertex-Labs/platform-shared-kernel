using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Reporting.Abstractions.Models;

/// <summary>
/// The successful outcome of <see cref="Exporters.IReportExporter{TRow}.ExportAsync"/>.
/// </summary>
public sealed record ReportExportOutcome
{
    /// <summary>The durable handle to the stored exported object — persist this, not <see cref="DownloadUrl"/>.</summary>
    public required FileReference StoredFile { get; init; }

    /// <summary>
    /// A presigned download URL for the exported object, populated only when the originating
    /// <see cref="ReportDestination.PresignedDownloadUrlExpiry"/> was non-<see langword="null"/>.
    /// </summary>
    public PresignedUrl? DownloadUrl { get; init; }

    /// <summary>
    /// The number of rows enumerated from the source during export, counted for free while
    /// streaming — no second pass over the row source is ever performed to compute this.
    /// </summary>
    public required long RowCount { get; init; }
}
