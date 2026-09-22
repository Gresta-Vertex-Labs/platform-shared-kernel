# SharedKernel.Reporting.Csv

RFC 4180 CSV report/data export for Platform.SharedKernel microservices — hand-written, **zero third-party NuGet dependencies**. The only one of this domain's three providers that is genuinely constant-memory end to end: encoding happens directly against the destination `Stream` via `StreamWriter`, one row at a time, as the `IAsyncEnumerable<TRow>` source is enumerated.

**This is the provider to reach for when the row count could be large.** See [`SharedKernel.Reporting.Abstractions`](../SharedKernel.Reporting.Abstractions/README.md) for the shared contract and column model.

## DI quick start

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("exports"); // any SharedKernel.Storage provider
builder.Services.AddCsvReportExporter<Invoice>(builder.Configuration);

var host = builder.Build();
await host.StartAsync();

var exporter = host.Services.GetRequiredService<ICsvReportExporter<Invoice>>();
```

`AddCsvReportExporter<TRow>` registers `CsvExportOptions` (validated, checked eagerly at startup via `ValidateOnStart()`), the shared `StorageStreamingWriter` (idempotent — safe alongside `AddSpreadsheetReportExporter`/`AddPdfReportExporter` in the same host), and `ICsvReportExporter<TRow>`. It does **not** register storage: call `AddSharedKernelStorage()` with a provider (`SharedKernel.Storage.S3`, `.Obs`) and register the stores your `ReportDestination.Store` values name.

`ICsvReportExporter<TRow>` is a provider-exclusive marker interface (`: IReportExporter<TRow>`) — injecting it against a composition root that never called `AddCsvReportExporter` fails to compile against the wrong assembly reference, never a runtime format-string check.

## Configuration

```json
{
  "SharedKernel": {
    "Reporting": {
      "Csv": {
        "IncludeUtf8Bom": true,
        "Delimiter": ","
      }
    }
  }
}
```

`IncludeUtf8Bom` defaults to `true`. RFC 4180 itself is silent on BOM — this is a deliberate platform default: the typical audience for a CSV export (a business user opening a statement in Excel) benefits from the BOM far more often than it is harmed by it.

## The RFC 4180 escaping guarantee

- Delimiter: `,` by default (configurable).
- A field is quoted when it contains the delimiter, a double quote, `\r`, or `\n`.
- An embedded double quote is escaped by doubling it.
- Line terminator: `\r\n`.

This single rule applies uniformly to every formatted string regardless of source type — a culture whose decimal separator is `,` (e.g. `de-DE`) produces a numeric field that already contains a comma, and the same "contains the delimiter → quote it" rule handles it correctly. There is no numeric-specific special case anywhere in this encoder.

## Why this is the safe choice for unbounded row counts

Every other provider in this domain (`.Spreadsheet` via ClosedXML, `.Pdf` via MigraDoc/PdfSharp) materializes its entire output document object model in memory before writing a byte — a verified, permanent characteristic of those third-party dependencies, not a defect. `SharedKernel.Reporting.Csv` has no such limitation: it never buffers more than one row's worth of formatted text at a time, and holds no document object model at all. If you don't know how many rows an export will produce, or you know it could be millions, this is the provider to use.
