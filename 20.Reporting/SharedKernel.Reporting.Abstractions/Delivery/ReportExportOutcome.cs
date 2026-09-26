using SharedKernel.Storage;

namespace SharedKernel.Reporting;

/// <summary>A report stored by <see cref="IReportExporter{TRow}.ExportAsync"/>.</summary>
public sealed record ReportExportOutcome
{
    /// <summary>
    /// Gets the durable handle to the stored object — store, tenant and key. Persist this, not
    /// <see cref="DownloadUrl"/>; open it later with <c>IFileStorageFactory.Open(StoredFile)</c>.
    /// </summary>
    public required FileReference StoredFile { get; init; }

    /// <summary>
    /// Gets the presigned download request, present only when <see cref="ReportDestination.PresignedDownloadUrlExpiry"/>
    /// was set.
    /// </summary>
    public PresignedRequest? DownloadUrl { get; init; }

    /// <summary>Gets the format the report was written in.</summary>
    public required ReportFormat Format { get; init; }

    /// <summary>Gets the number of rows exported, counted while streaming.</summary>
    public required long RowCount { get; init; }

    /// <summary>Gets the size of the stored object in bytes.</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>A report written to a stream by <see cref="IReportExporter{TRow}.ExportToStreamAsync"/>.</summary>
public sealed record ReportStreamOutcome
{
    /// <summary>Gets the format the report was written in.</summary>
    public required ReportFormat Format { get; init; }

    /// <summary>Gets the number of rows exported, counted while streaming.</summary>
    public required long RowCount { get; init; }

    /// <summary>Gets the number of bytes written to the stream.</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>A PDF stored by <see cref="IHtmlToPdfConverter.ConvertAsync"/>.</summary>
public sealed record PdfDocumentOutcome
{
    /// <summary>
    /// Gets the durable handle to the stored object — store, tenant and key. Persist this, not
    /// <see cref="DownloadUrl"/>.
    /// </summary>
    public required FileReference StoredFile { get; init; }

    /// <summary>
    /// Gets the presigned download request, present only when <see cref="ReportDestination.PresignedDownloadUrlExpiry"/>
    /// was set.
    /// </summary>
    public PresignedRequest? DownloadUrl { get; init; }

    /// <summary>Gets the size of the stored PDF in bytes.</summary>
    public required long SizeBytes { get; init; }
}
