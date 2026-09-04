namespace SharedKernel.Reporting.Abstractions.Models;

/// <summary>
/// Describes where <see cref="Exporters.IReportExporter{TRow}.ExportAsync"/> delivers its output via
/// <c>SharedKernel.Storage.Abstractions</c>' <c>IFileStorage</c>.
/// </summary>
public sealed record ReportDestination
{
    /// <summary>The destination bucket. Required, non-empty.</summary>
    public required string Bucket { get; init; }

    /// <summary>The destination object's key within <see cref="Bucket"/>. Required, non-empty.</summary>
    public required string Key { get; init; }

    /// <summary>Optional user-supplied metadata to store alongside the exported object.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// The time-to-live of a presigned download URL to generate for the exported object once
    /// upload completes. <see langword="null"/> (the default) means no presigned URL is requested,
    /// even when an <c>IBlobUriGenerator</c> is available to the exporter.
    /// </summary>
    public TimeSpan? PresignedDownloadUrlExpiry { get; init; }
}
