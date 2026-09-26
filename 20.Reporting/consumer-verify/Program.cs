// consumer-verify — exercises 20.Reporting's packages exactly as a downstream service composes them: one
// AddSharedKernelReporting() chain in a real IHost, storage through the real storage registry, and every output opened
// back with an independent reader. Surfaces:
//   1. The whole chain (CSV, Excel, PDF, Gotenberg) starts, validates its options, and resolves every exporter for any
//      row type — through the per-format interfaces, the factory, and keyed services
//   2. The factory parses a user-supplied format and picks the exporter at runtime
//   3. A CSV export is stored with its content type, download file name and a presigned link
//   4. An Excel export is stored and re-opens with ClosedXML, numbers as numbers
//   5. A PDF export is stored and re-opens with PDFsharp
// Gotenberg itself (a Docker service) is exercised by SharedKernel.Reporting.Gotenberg.Tests in the Integration lane.

using System.Collections.Concurrent;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfSharp.Pdf.IO;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Reporting.Csv;
using SharedKernel.Reporting.Pdf;
using SharedKernel.Reporting.Spreadsheet;
using SharedKernel.Storage;

var storage = new RecordingFileStorage();
using IHost host = await StartHostAsync(storage);

var definition = ReportDefinition.For<Row>()
    .Title("Orders")
    .Column("Id", r => r.Id)
    .Column("Customer", r => r.Name, relativeWidth: 2)
    .Column("Total", r => r.Total, format: "N2")
    .Build();

Surface1_EveryExporterResolves(host.Services);
Surface2_FactoryPicksTheFormatAtRuntime(host.Services);
await Surface3_CsvRoundTrip(host.Services, storage, definition);
await Surface4_SpreadsheetRoundTrip(host.Services, storage, definition);
await Surface5_PdfRoundTrip(host.Services, storage, definition);

await host.StopAsync();
Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

static async Task<IHost> StartHostAsync(RecordingFileStorage storage)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SharedKernel:Reporting:Csv:Delimiter"] = ";",
        ["SharedKernel:Reporting:Gotenberg:BaseUrl"] = "http://gotenberg:3000",
    });

    builder.Services.AddSharedKernelStorage().AddStore(new FileStoreRegistration(
        RecordingFileStorage.Name,
        tenantScoped: false,
        _ => storage,
        (_, _) => Task.FromResult(Result.Success())));

    builder.Services.AddSharedKernelReporting()
        .AddCsv(builder.Configuration)
        .AddSpreadsheet(builder.Configuration)
        .AddPdf(builder.Configuration)
        .AddGotenberg(builder.Configuration);

    IHost host = builder.Build();
    await host.StartAsync();
    return host;
}

static void Surface1_EveryExporterResolves(IServiceProvider services)
{
    Verify(services.GetRequiredService<ICsvReportExporter<Row>>().Format == ReportFormat.Csv, "ICsvReportExporter<Row> resolves");
    Verify(services.GetRequiredService<ISpreadsheetReportExporter<Row>>().Format == ReportFormat.Xlsx, "ISpreadsheetReportExporter<Row> resolves");
    Verify(services.GetRequiredService<IPdfReportExporter<string>>().Format == ReportFormat.Pdf, "IPdfReportExporter<T> resolves for any row type");
    Verify(services.GetRequiredKeyedService<IReportExporter<Row>>("xlsx").Format == ReportFormat.Xlsx, "keyed IReportExporter<Row> resolves");
    Verify(services.GetRequiredService<IHtmlToPdfConverter>() is not null, "IHtmlToPdfConverter resolves");
    Console.WriteLine("Surface 1 PASS: the whole chain starts and every exporter resolves for any row type");
}

static void Surface2_FactoryPicksTheFormatAtRuntime(IServiceProvider services)
{
    var factory = services.GetRequiredService<IReportExporterFactory>();
    Verify(factory.Formats.Count == 3, "three formats registered");
    Verify(factory.ParseFormat(".XLSX").Value == ReportFormat.Xlsx, "ParseFormat accepts an extension");
    Verify(factory.ParseFormat("text/csv").Value == ReportFormat.Csv, "ParseFormat accepts a content type");
    Verify(factory.ParseFormat("docx").Error.Code == ReportingErrorCodes.UnsupportedFormat, "an unknown format is a validation failure");
    Verify(factory.GetExporter<Row>(ReportFormat.Pdf) is IPdfReportExporter<Row>, "GetExporter returns the provider's exporter");
    Console.WriteLine("Surface 2 PASS: the factory parses a user-supplied format and picks the exporter");
}

static async Task Surface3_CsvRoundTrip(IServiceProvider services, RecordingFileStorage storage, ReportDefinition<Row> definition)
{
    Result<ReportExportOutcome> result = await services.GetRequiredService<ICsvReportExporter<Row>>().ExportAsync(
        Rows(new Row(1, "Ada", 10.5m), new Row(2, "=cmd|' /C calc'!A0", 2m)),
        definition,
        new ReportDestination
        {
            Store = RecordingFileStorage.Name,
            Key = "orders.csv",
            DownloadFileName = "Siparişler.csv",
            PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(5),
        });

    Verify(result.IsSuccess, "CSV ExportAsync succeeds");
    Verify(result.Value.RowCount == 2 && result.Value.DownloadUrl is not null, "outcome carries the row count and a download link");
    string csv = System.Text.Encoding.UTF8.GetString(storage.GetContent("orders.csv"));
    Verify(csv.Contains("1;Ada;10.50", StringComparison.Ordinal), "configured ';' delimiter and N2 format applied");
    Verify(csv.Contains(";'=cmd", StringComparison.Ordinal), "formula injection is neutralised");
    Verify(storage.GetOptions("orders.csv").ContentType == "text/csv", "content type stored");
    Verify(storage.GetOptions("orders.csv").ContentDisposition!.Contains("filename*=UTF-8''Sipari%C5%9Fler.csv", StringComparison.Ordinal), "download file name stored");
    Console.WriteLine("Surface 3 PASS: CSV stored with content type, download name and presigned link");
}

static async Task Surface4_SpreadsheetRoundTrip(IServiceProvider services, RecordingFileStorage storage, ReportDefinition<Row> definition)
{
    IReportExporter<Row> exporter = services.GetRequiredService<IReportExporterFactory>().GetExporter<Row>(ReportFormat.Xlsx);
    Result<ReportExportOutcome> result = await exporter.ExportAsync(
        Rows(new Row(1, "Ada", 1234.5m)),
        definition,
        new ReportDestination { Store = RecordingFileStorage.Name, Key = "orders.xlsx" });

    Verify(result.IsSuccess, "Excel ExportAsync succeeds");
    using var workbook = new XLWorkbook(new MemoryStream(storage.GetContent("orders.xlsx")));
    IXLWorksheet sheet = workbook.Worksheet(1);
    Verify(sheet.Name == "Orders", "the title names the sheet");
    Verify(sheet.Cell("C2").DataType == XLDataType.Number && sheet.Cell("C2").GetValue<decimal>() == 1234.5m, "totals are numbers");
    Console.WriteLine("Surface 4 PASS: Excel stored and re-opens via ClosedXML with typed cells");
}

static async Task Surface5_PdfRoundTrip(IServiceProvider services, RecordingFileStorage storage, ReportDefinition<Row> definition)
{
    Result<ReportExportOutcome> result = await services.GetRequiredService<IPdfReportExporter<Row>>().ExportAsync(
        Rows(new Row(1, "Ada", 1m), new Row(2, "Grace", 2m)),
        definition,
        new ReportDestination { Store = RecordingFileStorage.Name, Key = "orders.pdf" });

    Verify(result.IsSuccess, "PDF ExportAsync succeeds");
    using var pdf = PdfReader.Open(new MemoryStream(storage.GetContent("orders.pdf")), PdfDocumentOpenMode.Import);
    Verify(pdf.PageCount == 1 && pdf.Info.Title == "Orders", "PDF re-opens via PDFsharp with its title");
    Console.WriteLine("Surface 5 PASS: PDF stored and re-opens via PDFsharp");
}

static async IAsyncEnumerable<Row> Rows(params Row[] rows)
{
    foreach (Row row in rows)
    {
        await Task.Yield();
        yield return row;
    }
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }
}

internal sealed record Row(int Id, string Name, decimal Total);

/// <summary>
/// Minimal in-process store recording uploaded content and options, registered as the <c>verify</c> store through the
/// real storage registry — this harness deliberately avoids a 16.Testing reference, mirroring 08.Storage's own
/// consumer-verify. Members the exporters never call throw.
/// </summary>
internal sealed class RecordingFileStorage : IFileStorage
{
    public const string Name = "verify";

    private readonly ConcurrentDictionary<string, (byte[] Content, FileUploadOptions Options)> _store = new();

    public string StoreName => Name;

    public SharedKernel.Execution.Tenancy.TenantId? TenantId => null;

    public byte[] GetContent(string key) => _store[key].Content;

    public FileUploadOptions GetOptions(string key) => _store[key].Options;

    public async Task<Result<FileReference>> UploadAsync(string key, Stream content, FileUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _store[key] = (buffer.ToArray(), options ?? new FileUploadOptions());
        return Result<FileReference>.Success(new FileReference { Store = Name, Key = key });
    }

    public Task<Result<PresignedRequest>> CreateDownloadUrlAsync(string key, PresignedDownloadOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<PresignedRequest>.Success(new PresignedRequest
        {
            Url = new Uri($"https://verify.test/{Name}/{key}"),
            Method = "GET",
            Headers = new Dictionary<string, string>(),
            ExpiresAt = DateTimeOffset.UtcNow + options.Expiry,
        }));

    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(_store.ContainsKey(key)));

    public Task<Result<FileDownload>> DownloadAsync(string key, FileDownloadOptions? options = null, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<FileReference>> CopyAsync(string sourceKey, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<FileReference>> CopyToAsync(string sourceKey, IFileStorage destination, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default) => throw Unused();

    public IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<PresignedRequest>> CreateUploadUrlAsync(string key, PresignedUploadOptions options, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<PresignedPost>> CreateUploadFormAsync(string key, PresignedPostOptions options, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<MultipartUpload>> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(MultipartUpload upload, int partNumber, TimeSpan expiry, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result<FileReference>> CompleteMultipartUploadAsync(MultipartUpload upload, IReadOnlyCollection<UploadedPart> parts, WriteCondition? condition = null, CancellationToken cancellationToken = default) => throw Unused();

    public Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default) => throw Unused();

    private static NotSupportedException Unused() => new("consumer-verify's RecordingFileStorage only records uploads and presigns downloads.");
}
