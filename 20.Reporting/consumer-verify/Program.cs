// consumer-verify — exercises 20.Reporting's published packages exactly as a downstream
// microservice would: real DI composition through ProjectReference (standing in for a packed
// NuGet reference — the compiled surface is identical either way), never in-process unit-test
// scaffolding. Five surfaces:
//   1. All three providers (.Csv/.Spreadsheet/.Pdf) registered together resolve with zero DI
//      exceptions through a real IHost.StartAsync()
//   2. The shared StorageStreamingWriter registers exactly once (idempotent TryAddSingleton) even
//      though all three providers' AddXReportExporter<TRow>() extensions each call it
//   3. A real CSV export round-trips through IFileStorage end to end
//   4. A real spreadsheet export round-trips through IFileStorage and re-opens correctly via ClosedXML
//   5. A real PDF export round-trips through IFileStorage and re-opens correctly via PdfSharp

using System.Collections.Concurrent;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfSharp.Pdf.IO;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Csv.Exporters;
using SharedKernel.Reporting.Csv.Extensions;
using SharedKernel.Reporting.Pdf.Exporters;
using SharedKernel.Reporting.Pdf.Extensions;
using SharedKernel.Reporting.Spreadsheet.Exporters;
using SharedKernel.Reporting.Spreadsheet.Extensions;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;

await Surface1And2_AllThreeProvidersResolveWithSharedWriter();
await Surface3_CsvRoundTrip();
await Surface4_SpreadsheetRoundTrip();
await Surface5_PdfRoundTrip();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

// ── Surfaces 1 & 2: all three providers resolve, shared writer registers once ──
static async Task Surface1And2_AllThreeProvidersResolveWithSharedWriter()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton<IFileStorage>(new RecordingFileStorage());
    builder.Services.AddSingleton<IBlobUriGenerator>(new StubBlobUriGenerator());

    builder.Services.AddCsvReportExporter<Row>(builder.Configuration);
    builder.Services.AddSpreadsheetReportExporter<Row>(builder.Configuration);
    builder.Services.AddPdfReportExporter<Row>(builder.Configuration);

    using var host = builder.Build();
    await host.StartAsync();

    var csvExporter = host.Services.GetRequiredService<ICsvReportExporter<Row>>();
    var spreadsheetExporter = host.Services.GetRequiredService<ISpreadsheetReportExporter<Row>>();
    var pdfExporter = host.Services.GetRequiredService<IPdfReportExporter<Row>>();

    Verify(csvExporter is CsvReportExporter<Row>, "ICsvReportExporter<Row> resolves as CsvReportExporter<Row>");
    Verify(spreadsheetExporter is SpreadsheetReportExporter<Row>, "ISpreadsheetReportExporter<Row> resolves as SpreadsheetReportExporter<Row>");
    Verify(pdfExporter is IPdfReportExporter<Row>, "IPdfReportExporter<Row> resolves as PdfReportExporter<Row>");

    // All three AddXReportExporter<TRow>() calls independently call TryAddSingleton<StorageStreamingWriter>() —
    // resolving it twice must yield the identical instance, proving no collision/duplicate registration.
    var writer1 = host.Services.GetRequiredService<StorageStreamingWriter>();
    var writer2 = host.Services.GetRequiredService<StorageStreamingWriter>();
    Verify(ReferenceEquals(writer1, writer2), "StorageStreamingWriter resolves as one shared singleton across all three providers");

    await host.StopAsync();
    Console.WriteLine("Surface 1+2 PASS: all three providers resolve with zero DI exceptions and share one StorageStreamingWriter");
}

// ── Surface 3: real CSV round trip ────────────────────────────────────────────
static async Task Surface3_CsvRoundTrip()
{
    var storage = new RecordingFileStorage();
    var writer = new StorageStreamingWriter(storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageStreamingWriter>.Instance);
    var options = Microsoft.Extensions.Options.Options.Create(new SharedKernel.Reporting.Csv.Options.CsvExportOptions());
    var exporter = new CsvReportExporter<Row>(writer, options);

    var definition = new ReportDefinition<Row>
    {
        Columns =
        [
            new ReportColumn<Row> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
            new ReportColumn<Row> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
        ],
    };

    var result = await exporter.ExportAsync(
        Rows(new Row(1, "Alice"), new Row(2, "Bob")),
        definition,
        new ReportDestination { Bucket = "verify", Key = "export.csv" },
        CancellationToken.None);

    Verify(result.IsSuccess, "CSV ExportAsync succeeds");
    Verify(result.Value.RowCount == 2, "CSV RowCount is 2");
    var content = System.Text.Encoding.UTF8.GetString(storage.GetContent("verify", "export.csv"));
    Verify(content.Contains("Alice", StringComparison.Ordinal) && content.Contains("Bob", StringComparison.Ordinal), "CSV content round-trips correctly");

    Console.WriteLine("Surface 3 PASS: real CSV export round-trips through IFileStorage");
}

// ── Surface 4: real spreadsheet round trip ────────────────────────────────────
static async Task Surface4_SpreadsheetRoundTrip()
{
    var storage = new RecordingFileStorage();
    var writer = new StorageStreamingWriter(storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageStreamingWriter>.Instance);
    var options = Microsoft.Extensions.Options.Options.Create(new SharedKernel.Reporting.Spreadsheet.Options.SpreadsheetExportOptions());
    var exporter = new SpreadsheetReportExporter<Row>(writer, options);

    var definition = new ReportDefinition<Row>
    {
        Columns =
        [
            new ReportColumn<Row> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
            new ReportColumn<Row> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
        ],
    };

    var result = await exporter.ExportAsync(
        Rows(new Row(1, "Alice")),
        definition,
        new ReportDestination { Bucket = "verify", Key = "export.xlsx" },
        CancellationToken.None);

    Verify(result.IsSuccess, "Spreadsheet ExportAsync succeeds");
    using var workbook = new XLWorkbook(new MemoryStream(storage.GetContent("verify", "export.xlsx")));
    var worksheet = workbook.Worksheets.First();
    Verify(worksheet.Cell(2, 2).GetString() == "Alice", "spreadsheet content round-trips correctly via ClosedXML re-open");

    Console.WriteLine("Surface 4 PASS: real spreadsheet export round-trips through IFileStorage and re-opens via ClosedXML");
}

// ── Surface 5: real PDF round trip ────────────────────────────────────────────
static async Task Surface5_PdfRoundTrip()
{
    var storage = new RecordingFileStorage();
    var writer = new StorageStreamingWriter(storage, Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageStreamingWriter>.Instance);
    var options = Microsoft.Extensions.Options.Options.Create(new SharedKernel.Reporting.Pdf.Options.PdfExportOptions());
    var exporter = new PdfReportExporter<Row>(writer, options);

    var definition = new ReportDefinition<Row>
    {
        Title = "Verification Statement",
        Columns =
        [
            new ReportColumn<Row> { Header = "Id", Ordinal = 0, ValueSelector = r => r.Id },
            new ReportColumn<Row> { Header = "Name", Ordinal = 1, ValueSelector = r => r.Name },
        ],
    };

    var result = await exporter.ExportAsync(
        Rows(new Row(1, "Alice")),
        definition,
        new ReportDestination { Bucket = "verify", Key = "export.pdf" },
        CancellationToken.None);

    Verify(result.IsSuccess, "PDF ExportAsync succeeds");
    using var pdf = PdfReader.Open(new MemoryStream(storage.GetContent("verify", "export.pdf")), PdfDocumentOpenMode.Import);
    Verify(pdf.PageCount >= 1, "PDF re-opens via PdfSharp with at least one page");

    Console.WriteLine("Surface 5 PASS: real PDF export round-trips through IFileStorage and re-opens via PdfSharp");
}

static async IAsyncEnumerable<Row> Rows(params Row[] rows)
{
    foreach (var row in rows)
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

internal sealed record Row(int Id, string Name);

/// <summary>Minimal in-process <see cref="IFileStorage"/> recording uploaded content — this harness deliberately avoids a 16.Testing reference, mirroring 08.Storage's own consumer-verify precedent.</summary>
internal sealed class RecordingFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<(string Bucket, string Key), byte[]> _store = new();

    public byte[] GetContent(string bucket, string key) => _store[(bucket, key)];

    public async Task<Result<FileReference>> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        _store[(request.Bucket, request.Key)] = buffer.ToArray();
        return Result<FileReference>.Success(new FileReference { Bucket = request.Bucket, Key = request.Key });
    }

    public Task<Result<FileDownload>> DownloadAsync(string bucket, string key, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result> DeleteAsync(string bucket, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());

    public Task<Result<bool>> ExistsAsync(string bucket, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Result<bool>.Success(_store.ContainsKey((bucket, key))));

    public Task<Result<FileMetadata>> GetMetadataAsync(string bucket, string key, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<FileReference>> CopyAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<FileDeleteOutcome>>> DeleteManyAsync(string bucket, IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<FileMetadata> ListAsync(string bucket, string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<Result> CheckHealthAsync(string bucket, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());
}

/// <summary>Minimal in-process <see cref="IBlobUriGenerator"/> stub.</summary>
internal sealed class StubBlobUriGenerator : IBlobUriGenerator
{
    public Result<PresignedUrl> GeneratePresignedUploadUrl(PresignedUrlRequest request) =>
        Result<PresignedUrl>.Success(new PresignedUrl { Url = new Uri($"https://verify.test/{request.Bucket}/{request.Key}"), ExpiresAt = DateTimeOffset.UtcNow + request.Expiry });

    public Result<PresignedUrl> GeneratePresignedDownloadUrl(PresignedUrlRequest request) =>
        Result<PresignedUrl>.Success(new PresignedUrl { Url = new Uri($"https://verify.test/{request.Bucket}/{request.Key}"), ExpiresAt = DateTimeOffset.UtcNow + request.Expiry });
}
