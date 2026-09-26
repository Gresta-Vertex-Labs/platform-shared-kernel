# SharedKernel.Reporting.Abstractions

Report and document export for Platform.SharedKernel services: stream rows into **CSV, Excel or PDF**, or render
**HTML to PDF**, and deliver the file to a named `SharedKernel.Storage` store (with a presigned download link) or to
any stream. This package holds the contracts, the fluent report definition, the bases for custom formats, the
delivery pipeline and the telemetry; no third-party dependencies.

| Provider | Format | Memory |
|---|---|---|
| [`SharedKernel.Reporting.Csv`](../SharedKernel.Reporting.Csv/README.md) | CSV (RFC 4180) | constant |
| [`SharedKernel.Reporting.Spreadsheet`](../SharedKernel.Reporting.Spreadsheet/README.md) | Excel `.xlsx` (typed cells) | constant |
| [`SharedKernel.Reporting.Pdf`](../SharedKernel.Reporting.Pdf/README.md) | tabular PDF (statements, lists) | in memory, capped by `MaxRows` |
| [`SharedKernel.Reporting.Gotenberg`](../SharedKernel.Reporting.Gotenberg/README.md) | HTML → PDF (invoices, letters) | streamed |

```xml
<PackageReference Include="SharedKernel.Reporting.Abstractions" />
```

Versions come from the consumer's single `SharedKernelVersion`. **Tier: Abstractions** — an Application project may
reference it. For unit tests, [`SharedKernel.Reporting.Testing`](../../16.Testing/SharedKernel.Reporting.Testing/README.md)
has in-memory fakes.

## Register

```csharp
builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");

builder.Services.AddSharedKernelReporting()
    .AddCsv(builder.Configuration)            // SharedKernel.Reporting.Csv
    .AddSpreadsheet(builder.Configuration)    // SharedKernel.Reporting.Spreadsheet
    .AddPdf(builder.Configuration)            // SharedKernel.Reporting.Pdf
    .AddGotenberg(builder.Configuration);     // SharedKernel.Reporting.Gotenberg — IHtmlToPdfConverter

builder.WithReportingTelemetry();             // SharedKernel.ServiceDefaults
```

Each format is registered once and serves **every row type**. Add only the formats you use. Storage is optional:
without it, `ExportToStreamAsync` works and `ExportAsync` throws an explaining `InvalidOperationException`.

## Define a report

```csharp
ReportDefinition<Order> definition = ReportDefinition.For<Order>()
    .Title("Orders — September")
    .Culture(CultureInfo.GetCultureInfo("tr-TR"))                  // numbers and dates, not translation
    .Column("Order", o => o.Number)
    .Column("Placed", o => o.PlacedAt, format: "yyyy-MM-dd")
    .Column("Customer", o => o.CustomerName, relativeWidth: 3)
    .Column("Total", o => o.Total, format: "N2")                   // numbers right-align automatically
    .Column("Status", o => o.Status, (status, culture) => status.ToDisplayText())
    .Build();
```

- Columns render in the order added. `null` is an empty cell, never `"null"`.
- `format` is a .NET format string. CSV and PDF format the value as text; Excel keeps numbers, dates, times and
  booleans as **typed cells** and translates the format to an Excel number format.
- A column with a formatter function is text in every format.
- `alignment` (`Auto`, `Left`, `Center`, `Right`) and `relativeWidth` shape PDF and Excel; CSV ignores them.
- Headers are yours to translate before building the definition.

## Export

Inject one format's exporter, or pick the format at runtime:

```csharp
public sealed class ExportOrdersHandler(IReportExporterFactory exporters, IOrderQueries orders, IRequestContext caller)
{
    public async Task<Result<ReportExportOutcome>> Handle(ExportOrders command, CancellationToken ct)
    {
        Result<ReportFormat> format = exporters.ParseFormat(command.Format);   // "xlsx", ".csv", "application/pdf"…
        if (format.IsFailure)
        {
            return format.Error;                                               // reporting.unsupported_format
        }

        return await exporters.GetExporter<Order>(format.Value).ExportAsync(
            orders.StreamAsync(command.Month, ct),                             // IAsyncEnumerable<Order> — never a List
            Definition,
            new ReportDestination
            {
                Store = "reports",
                TenantId = caller.TenantId,                                    // for a tenant store
                Key = format.Value.WithExtension($"orders/{Guid.CreateVersion7():N}"),
                DownloadFileName = format.Value.WithExtension("Orders September"),
                PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15),
            },
            ct);
    }
}
```

Or inject `ICsvReportExporter<Order>` / `ISpreadsheetReportExporter<Order>` / `IPdfReportExporter<Order>`, or the keyed
service `[FromKeyedServices("xlsx")] IReportExporter<Order>`.

`ReportExportOutcome` carries `StoredFile` (persist this `FileReference`), `DownloadUrl` (a `PresignedRequest`, when
requested), `Format`, `RowCount` and `SizeBytes`. `ExportToStreamAsync` writes to any stream instead — an HTTP
response, a `MemoryStream` for an e-mail attachment — and returns `ReportStreamOutcome`.

### Destination

| Property | |
|---|---|
| `Store`, `Key` | Required. An unknown store throws (configuration error). |
| `TenantId` | Required exactly for a tenant store; take it from `IRequestContext`, never from input. |
| `DownloadFileName` | Stored as `Content-Disposition: attachment` (UTF-8 names supported); presigned links download under it. |
| `Condition` | e.g. `WriteCondition.IfNotExists` — never overwrite an issued statement. |
| `Metadata` | User metadata on the object. |
| `PresignedDownloadUrlExpiry` | Creates a download link; must not exceed the store's maximum. |

### How delivery works

The exporter writes into a pipe whose other end is `IFileStorage.UploadAsync`: bytes reach storage **while they are
produced**, never buffered as a whole file. If the export fails part-way (a row limit, an exception) the upload is
aborted — **a half-written report is never stored**. If the store rejects the upload early (a failed condition), the
exporter stops at its next write instead of reading the rest of the rows.

## HTML to PDF

```csharp
Result<PdfDocumentOutcome> stored = await converter.ConvertAsync(       // IHtmlToPdfConverter
    html,
    new ReportDestination { Store = "invoices", Key = "2026-0042.pdf", Condition = WriteCondition.IfNotExists },
    new HtmlToPdfOptions { PageSize = PdfPageSize.A4, FooterHtml = HtmlToPdfOptions.PageNumberFooter },
    ct);
```

`HtmlToPdfOptions`: `PageSize` (A3/A4/A5/Letter/Legal or custom mm), `Landscape`, `Margins` (mm), `PrintBackground`,
`Scale`, `PreferCssPageSize`, `HeaderHtml`/`FooterHtml` (placeholders `pageNumber`, `totalPages`, `date`, `title`), and
`Assets` (images, fonts, stylesheets referenced by file name). **HTML-encode user data** before it enters the markup.
See [`SharedKernel.Reporting.Gotenberg`](../SharedKernel.Reporting.Gotenberg/README.md).

## Failures

Expected failures are `Result`s; the codes are constants in `ReportingErrorCodes`:

| Code | Type | When |
|---|---|---|
| `reporting.invalid_definition` | Validation | No columns, or a column without header or value, or a bad width |
| `reporting.invalid_destination` | Validation | Missing store or key, `default(TenantId)`, bad file name or expiry |
| `reporting.unsupported_format` | Validation | `ParseFormat` did not match a registered format |
| `reporting.row_limit_exceeded` | Validation | More rows than the format's `MaxRows`; nothing is stored |
| `reporting.invalid_request` | Validation | Empty HTML or invalid conversion options |
| `reporting.conversion_failed` | Validation | The converter refused the document |
| `reporting.converter_unavailable` | Unavailable | The converter could not be reached or failed |
| `reporting.conversion_timeout` | Timeout | The conversion took too long |

Storage failures keep their `storage.*` codes. An exception thrown by the row source, a value function or a formatter
propagates — it is a bug, not an outcome.

## Personal data

An export is a bulk copy into a durable, shareable file. This library classifies and redacts nothing: redact with
`SharedKernel.DataPrivacy` in the query that produces the rows. Logs, spans and metrics never contain row values, keys
or file names.

## Telemetry

`WithReportingTelemetry()` exports the `SharedKernel.Reporting` source and meter:

- Spans `reporting export` and `reporting convert`, tagged `reporting.format`, `reporting.operation`, `reporting.store`,
  `reporting.row_count`, `reporting.size_bytes`, and on failure `error.type` with an error status.
- `reporting.operation.duration` (s), `reporting.rows`, `reporting.bytes`.
- Logs (category `SharedKernel.Reporting`, EventIds 20000–20006): started (Debug), completed (Information), failed with
  its error code (Warning), threw (Error), cancelled (Debug), download link failed (Warning), PDF converted.

## A custom format

Derive from `ReportExporterBase<TRow>` — validation, delivery, tracing and logging are done for you:

```csharp
internal sealed class JsonLinesExporter<TRow>(ReportingDependencies dependencies) : ReportExporterBase<TRow>(dependencies)
{
    public static readonly ReportFormat JsonLines = new("jsonl", "application/x-ndjson", ".jsonl");

    public override ReportFormat Format => JsonLines;

    protected override async Task<Result<long>> EncodeAsync(
        IAsyncEnumerable<TRow> rows, ReportDefinition<TRow> definition, Stream destination, CancellationToken ct)
    {
        long count = 0;
        await foreach (TRow row in rows.WithCancellation(ct))
        {
            // write one JSON object per line to destination (write-only, not seekable; never dispose it)
            count++;
        }

        return count;                                        // or a failure such as ReportingErrors.RowLimitExceeded
    }
}

builder.Services.AddSharedKernelReporting().AddExporter(JsonLinesExporter<object>.JsonLines, typeof(JsonLinesExporter<>));
```

`HtmlToPdfConverterBase` (`RenderAsync`) plus `AddHtmlToPdfConverter<T>()` does the same for another HTML engine.
