using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Reports;

/// <summary>
/// Exports a store's listing as a report in the format the caller asks for, stores it in the same store under
/// <c>reports/</c>, and returns a short-lived download link.
/// </summary>
/// <param name="Store">The store to list, and to write the report to.</param>
/// <param name="Format">The requested format — <c>csv</c>, <c>xlsx</c> or <c>pdf</c> (a name, extension or content type).</param>
/// <param name="Prefix">Only keys under this prefix are listed.</param>
public sealed record ExportListing(StoreAddress Store, string? Format, string Prefix) : ICommand<ExportedReport>;

/// <summary>A stored report: where it is, what it holds, and a link to download it.</summary>
/// <param name="Key">The report's key in the store.</param>
/// <param name="Format">The format's name.</param>
/// <param name="RowCount">The number of rows.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="Download">A presigned GET, valid for ten minutes.</param>
public sealed record ExportedReport(string Key, string Format, long RowCount, long SizeBytes, PresignedRequest? Download);

public sealed class ExportListingHandler(IFileStorageFactory storage, IReportExporterFactory exporters)
    : ICommandHandler<ExportListing, ExportedReport>
{
    public const string ReportsPrefix = "reports/";

    // One definition, shared by every format: CSV and PDF format the values as text, Excel keeps them typed.
    private static readonly ReportDefinition<FileListItem> Definition = ReportDefinition.For<FileListItem>()
        .Title("Files")
        .Column("Key", f => f.Key, relativeWidth: 4)
        .Column("Size (bytes)", f => f.ContentLength, format: "N0")
        .Column("Last modified (UTC)", f => f.LastModified?.UtcDateTime, format: "yyyy-MM-dd HH:mm")
        .Build();

    public async Task<Result<ExportedReport>> Handle(ExportListing command, CancellationToken cancellationToken)
    {
        Result<ReportFormat> format = exporters.ParseFormat(command.Format ?? ReportFormat.Csv.Name);
        if (format.IsFailure)
        {
            return format.Error;
        }

        Result<IFileStorage> files = Stores.Resolve(storage, command.Store);
        if (files.IsFailure)
        {
            return files.Error;
        }

        // The listing streams page by page from the provider straight into the exporter; nothing is collected first.
        Result<ReportExportOutcome> exported = await exporters.GetExporter<FileListItem>(format.Value).ExportAsync(
            files.Value.ListAsync(command.Prefix, cancellationToken),
            Definition,
            new ReportDestination
            {
                Store = files.Value.StoreName,
                TenantId = files.Value.TenantId,
                Key = format.Value.WithExtension($"{ReportsPrefix}{Guid.CreateVersion7():N}"),
                DownloadFileName = format.Value.WithExtension("files"),
                PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(10),
            },
            cancellationToken);

        return exported.IsFailure
            ? exported.Error
            : new ExportedReport(
                exported.Value.StoredFile.Key,
                exported.Value.Format.Name,
                exported.Value.RowCount,
                exported.Value.SizeBytes,
                exported.Value.DownloadUrl);
    }
}
