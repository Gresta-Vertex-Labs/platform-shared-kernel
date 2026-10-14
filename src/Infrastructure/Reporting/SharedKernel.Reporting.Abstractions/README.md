# SharedKernel.Reporting.Abstractions

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Abstractions](https://img.shields.io/badge/tier-Abstractions-1f6feb)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Streaming](https://img.shields.io/badge/rows-streamed-success)

> **Stream rows into CSV, Excel or PDF — or render HTML to PDF — and deliver the file to a named storage store with a
> presigned download link, or to any stream. The export is written while it is produced, never held as a whole in
> memory, and never stored half-written.** Reference it from your Application project; add the format packages
> (`.Csv`, `.Spreadsheet`, `.Pdf`, `.Gotenberg` for HTML → PDF) in Infrastructure.

| You get | So that |
| --- | --- |
| `ReportDefinition.For<T>().Column(…).Build()` | A report layout is a few typed lines, shared by every format |
| `IReportExporter<TRow>` over `IAsyncEnumerable<TRow>` | A million-row export costs the memory of one row (CSV, Excel) |
| `IReportExporterFactory.ParseFormat(userInput)` | A user-chosen `?format=` (csv, xlsx, pdf) needs no switch statement |
| Delivery to a `SharedKernel.Storage` store, with `DownloadFileName` and a presigned link | The caller gets a `FileReference` to persist and a URL to hand out |
| Pipe-based upload that aborts on failure | A report that failed half-way is never stored |
| `IHtmlToPdfConverter` + `HtmlToPdfOptions` | Invoices and letters render from HTML the service already produces |
| `ReportExporterBase<T>`, `HtmlToPdfConverterBase` | A new format or engine only encodes bytes; validation, delivery and telemetry come with the base |
| No third-party dependency | Application code can reference it without pulling in a format library |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Reporting.Abstractions" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Abstractions — reference it from your **Application** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Storage.Abstractions` |
| Namespaces | `SharedKernel.Reporting` |
| Providers | [Csv](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Csv/README.md) · [Spreadsheet](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Spreadsheet/README.md) · [Pdf](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Pdf/README.md) · [Gotenberg](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Gotenberg/README.md) (HTML → PDF) |

## Quick start

Register storage, then the formats you use (each provider package adds its `Add…` method):

```csharp
using SharedKernel.Reporting;
using SharedKernel.Storage;

builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");

builder.Services.AddSharedKernelReporting()
    .AddCsv(builder.Configuration)            // SharedKernel.Reporting.Csv
    .AddSpreadsheet(builder.Configuration)    // SharedKernel.Reporting.Spreadsheet
    .AddPdf(builder.Configuration);           // SharedKernel.Reporting.Pdf

// Spans and instruments are subscribed by the SharedKernel.* wildcard of AddServiceDefaults.
```

Define the report once and export it in whatever format the user asked for:

```csharp
using System.Globalization;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;

public sealed class ExportOrdersHandler(IReportExporterFactory exporters, IOrderQueries orders, IRequestContext caller)
{
    private static readonly ReportDefinition<Order> Definition = ReportDefinition.For<Order>()
        .Title("Orders")
        .Culture(CultureInfo.GetCultureInfo("tr-TR"))           // number and date formatting, not translation
        .Column("Order", o => o.Number)
        .Column("Placed", o => o.PlacedAt, format: "yyyy-MM-dd")
        .Column("Customer", o => o.CustomerName, relativeWidth: 3)
        .Column("Total", o => o.Total, format: "N2")            // numbers right-align automatically
        .Build();

    public async Task<Result<ReportExportOutcome>> Handle(ExportOrders command, CancellationToken ct)
    {
        Result<ReportFormat> format = exporters.ParseFormat(command.Format);   // "xlsx", ".csv", "application/pdf"
        if (format.IsFailure)
            return format.Error;                                               // reporting.format_not_supported

        return await exporters.GetExporter<Order>(format.Value).ExportAsync(
            orders.StreamAsync(command.Month, ct),                             // IAsyncEnumerable<Order>
            Definition,
            new ReportDestination
            {
                Store = "reports",
                Key = format.Value.WithExtension($"orders/{Guid.CreateVersion7():N}"),
                DownloadFileName = format.Value.WithExtension("Orders September"),
                PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(15),
            },
            ct);
    }
}
```

`ReportExportOutcome` carries `StoredFile` (persist this `FileReference`), `DownloadUrl` (a `PresignedRequest`, when
requested), `Format`, `RowCount` and `SizeBytes`.

## How it works

```mermaid
flowchart LR
    Q["IAsyncEnumerable&lt;TRow&gt;<br/>(your query)"] --> E["Exporter<br/>EncodeAsync"]
    D["ReportDefinition&lt;TRow&gt;"] --> E
    E -- "bytes as produced" --> P["Pipe"]
    P --> U["IFileStorage.UploadAsync<br/>(named or tenant store)"]
    U --> O["ReportExportOutcome<br/>FileReference + presigned URL"]
    E -. "failure / exception" .-> X["pipe faulted → upload aborted,<br/>nothing stored"]
```

- **Streaming.** The exporter writes into a `System.IO.Pipelines` pipe whose reader is `IFileStorage.UploadAsync`, so
  bytes reach storage while they are produced. CSV and Excel encode in constant memory; PDF is built in memory and
  capped by its `MaxRows`.
- **Never half-written.** A failure part-way (a row limit, a thrown exception) faults the pipe and the upload is
  aborted. If the store stops the upload early (a failed `WriteCondition`), the exporter stops at its next write
  instead of reading the rest of the rows.
- **Formatting is culture, not translation.** A column's `format` is a .NET format string applied under the report's
  `Culture`. `null` is an empty cell. Headers are whatever strings you pass — translate them first.
- **One registration per format** serves every row type (an open generic), so `ICsvReportExporter<Order>` and
  `ICsvReportExporter<Invoice>` need nothing more.
- **Content disposition.** `DownloadFileName` is stored as an RFC 6266 `Content-Disposition: attachment` with a UTF-8
  `filename*`, so presigned links download under that name, non-ASCII included.
- **Nothing sensitive in telemetry.** Logs, spans and metrics carry format, operation, store, row count, size and
  error code — never row values, object keys or file names. This package redacts nothing: filter personal data in
  the query that produces the rows.
- **Expected failures are `Result`s**; an exception from the row source, a value function or a formatter propagates.
  A missing storage registration or an unknown store throws `InvalidOperationException` (a configuration error).
  `ReportDefinitionBuilder.Build()` throws `InvalidReportDefinitionException` (a classified `reporting.invalid_definition`
  `Validation` failure, so an edge answers 400) when the definition has no columns or an invalid column.

## Recipes

### 1. Stream an export into the HTTP response

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;   // IReportExporter<Order> exporter

Result<ReportStreamOutcome> written = await exporter.ExportToStreamAsync(rows, Definition, httpContext.Response.Body, ct);
```

Storage is optional for this path: without it `ExportToStreamAsync` still works, and `ExportAsync` throws an
explaining `InvalidOperationException`.

### 2. Store a statement once, in a tenant store

```csharp
using SharedKernel.Reporting;
using SharedKernel.Storage;

new ReportDestination
{
    Store = "statements",                       // registered with AddTenantStore("statements")
    TenantId = caller.TenantId,                 // from IRequestContext, never from input
    Key = "2026-09.pdf",
    Condition = WriteCondition.IfNotExists,     // an issued statement is never overwritten
};
```

### 3. Render an invoice from HTML

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Storage;

Result<PdfDocumentOutcome> stored = await converter.ConvertAsync(       // IHtmlToPdfConverter
    html,                                                               // HTML-encode every user value first
    new ReportDestination { Store = "invoices", Key = "2026-0042.pdf", Condition = WriteCondition.IfNotExists },
    new HtmlToPdfOptions { PageSize = PdfPageSize.A4, FooterHtml = HtmlToPdfOptions.PageNumberFooter },
    ct);
```

`HtmlToPdfOptions`: `PageSize` (`A3`, `A4`, `A5`, `Letter`, `Legal`, or `new PdfPageSize(widthMm, heightMm)`),
`Landscape`, `Margins` (`PdfMargins`, mm), `PrintBackground`, `Scale`, `PreferCssPageSize`, `HeaderHtml`/`FooterHtml`
(placeholders `pageNumber`, `totalPages`, `date`, `title`) and `Assets` (`HtmlAsset(fileName, bytes)`, referenced by
file name). The implementation is [`SharedKernel.Reporting.Gotenberg`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Gotenberg/README.md).

### 4. Add a custom format

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;

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
            // write one JSON object per line (destination is write-only and not seekable; never dispose it)
            count++;
        }
        return count;                            // or a failure such as ReportingErrors.RowLimitExceeded(...)
    }
}

builder.Services.AddSharedKernelReporting()
    .AddExporter(JsonLinesExporter<object>.JsonLines, typeof(JsonLinesExporter<>));
```

`HtmlToPdfConverterBase` (`RenderAsync`) plus `AddHtmlToPdfConverter<T>()` does the same for another HTML engine.

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddSharedKernelReporting()` | `IReportExporterFactory`; returns `IReportingBuilder` |
| `AddExporter(ReportFormat, Type openGenericExporter)` | An exporter for every row type, keyed by the format name (`[FromKeyedServices("xlsx")] IReportExporter<T>`) |
| `AddHtmlToPdfConverter<TConverter>()` | The `IHtmlToPdfConverter` |

### Main types

| Type | Purpose |
| --- | --- |
| `ReportDefinition.For<T>()` → `ReportDefinitionBuilder<T>` | `Title`, `Culture`, `Column(header, value, format:, alignment:, relativeWidth:)`, `Column(header, value, formatter)`, `Build()` |
| `ReportColumnAlignment` | `Auto` (numbers right), `Left`, `Center`, `Right` — PDF and Excel; CSV ignores it |
| `IReportExporter<TRow>` | `ExportAsync` (to a `ReportDestination`) → `ReportExportOutcome`; `ExportToStreamAsync` → `ReportStreamOutcome` |
| `IReportExporterFactory` | `Formats`, `ParseFormat(value)` (a name, extension or content type) → `Result<ReportFormat>`, `GetExporter<T>(format)` |
| `ReportFormat` | `Csv`, `Xlsx`, `Pdf`, or your own; `WithExtension(fileName)` |
| `ReportDestination` | `Store`, `Key` (required), `TenantId`, `DownloadFileName`, `Condition`, `Metadata`, `PresignedDownloadUrlExpiry` |
| `IHtmlToPdfConverter` | `ConvertAsync` → `PdfDocumentOutcome`; `ConvertToStreamAsync` |
| `ReportValueFormatting` | The formatting rules the built-in formats use, for custom exporters |
| `InvalidReportDefinitionException` | Thrown by `Build()`; carries the `reporting.invalid_definition` `Error` |
| `ReportingDiagnostics` | `ActivitySourceName` and `MeterName` (`SharedKernel.Reporting`), for subscribing and asserting |

### Errors

`ReportingErrorCodes` / `ReportingErrors`; storage failures keep their `storage.*` codes.

| Code | Type | When |
| --- | --- | --- |
| `reporting.invalid_definition` | Validation | No columns, a column without header or value, or a bad width |
| `reporting.invalid_destination` | Validation | Missing store or key, `default(TenantId)`, bad file name or expiry |
| `reporting.format_not_supported` | Validation | `ParseFormat` matched no registered format |
| `reporting.row_limit_exceeded` | Validation | More rows than the format's `MaxRows`; nothing is stored |
| `reporting.invalid_request` | Validation | Empty HTML or invalid conversion options |
| `reporting.conversion_failed` | Validation | The converter refused the document ("The document could not be converted.") |
| `reporting.converter_unavailable` | Unavailable | The converter could not be reached or failed ("The document converter is unavailable.") |
| `reporting.conversion_timeout` | Timeout | The conversion took too long |

### Logging

Category `SharedKernel.Reporting.ReportingDependencies` (`ILogger<ReportingDependencies>`). Every template is one
sentence; per-operation success is Debug.

| Event id | Level | Template |
| --- | --- | --- |
| 20000 | Debug | `Report {Operation} for format '{Format}' started.` |
| 20001 | Debug | `Report {Operation} for format '{Format}' produced {RowCount} rows and {SizeBytes} bytes in {ElapsedMilliseconds} ms.` |
| 20002 | Warning | `Report {Operation} for format '{Format}' failed with error code '{ErrorCode}' of type '{ErrorType}'.` |
| 20003 | Error | `Report {Operation} for format '{Format}' threw an exception.` (exception attached) |
| 20004 | Debug | `Report {Operation} for format '{Format}' was cancelled.` |
| 20005 | Warning | `The report was stored in store '{Store}' but its download link could not be created, error code '{ErrorCode}'.` |
| 20006 | Debug | `PDF conversion produced {SizeBytes} bytes in {ElapsedMilliseconds} ms.` |

Block 20000-20099 belongs to this package; `.Csv` (20100), `.Spreadsheet` (20200) and `.Pdf` (20300) log nothing and
stay reserved, `.Gotenberg` owns 20400-20499. Nothing logged carries row values, object keys or file names.

### Telemetry

`ReportingDiagnostics.ActivitySourceName` and `ReportingDiagnostics.MeterName` are both `SharedKernel.Reporting`; the
`SharedKernel.*` wildcard of `AddServiceDefaults` subscribes to them (there is no `WithReportingTelemetry()`). Spans
`reporting export` and `reporting convert` (kind Internal; tags `reporting.format`, `reporting.operation`,
`reporting.store`, `reporting.row_count`, `reporting.size_bytes`). A returned failure sets status Error with the error
code as description, `error.code` and `error.type` (the `ErrorType`, for example `Validation`); an exception adds the
full exception event and `error.type` is its type name; cancellation is never an error.

| Instrument | Unit | Tags | When |
| --- | --- | --- | --- |
| `sharedkernel.reporting.operation.duration` (histogram) | `s` | `reporting.format`, `reporting.operation`, `outcome` (`success`, `failure`, `exception`, `canceled`), plus `error.type` on `failure` and `exception` | every call |
| `sharedkernel.reporting.rows` (counter) | `{row}` | `reporting.format` | successful exports |
| `sharedkernel.reporting.bytes` (counter) | `By` | `reporting.format`, `reporting.operation` | successful exports and conversions |

The meter comes from `IMeterFactory` when the host registers one (`AddMetrics()`, part of the Host builders) and is
created directly otherwise.

## Testing

Reference [`SharedKernel.Reporting.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/SharedKernel.Reporting.Testing/README.md)
and call `services.AddInMemoryReporting()`: it replaces `IReportExporterFactory` with `InMemoryReportExporterFactory`
and `IHtmlToPdfConverter` with `InMemoryHtmlToPdfConverter`. `factory.Exporter<T>(ReportFormat.Csv)` returns the
`InMemoryReportExporter<T>`, which records `LastRows`, `LastDefinition`, `LastDestination` and `ExportCount`, asserts
with `ShouldHaveExported(rows => …)`, and fails on demand (`SimulateFailure`, `SimulatedError`). The converter records
`Conversions` and writes a placeholder PDF.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Materialize rows into a `List<T>` first | Pass the query's `IAsyncEnumerable<T>` (`StreamAsync`, `ListKeysetAsync`); `ToAsyncEnumerable()` if you already hold a list | Streaming is the whole point; there is no list overload on purpose |
| Take `TenantId` from the request body | Take it from `IRequestContext` | The tenant store must be the caller's |
| Overwrite issued documents | `Condition = WriteCondition.IfNotExists` | A statement or invoice must not change after issue |
| Put raw user input into HTML | HTML-encode it | The HTML runs in a browser |
| Export personal data unfiltered | Redact in the query (`SharedKernel.DataPrivacy`) | An export is a durable, shareable copy |
| Rely on translated headers | Translate headers before `Column(…)` | The definition formats values; it does not translate |
| Export tens of thousands of rows as PDF | CSV or Excel for bulk data | PDF is built in memory and capped by `MaxRows` |

## Design decisions

**Why only `IAsyncEnumerable<TRow>`?** Once a list overload exists every caller uses it, and memory grows with the
report. A caller holding a list converts it in one call.

**Why deliver through a pipe instead of a temporary file?** Bytes go straight to object storage without local disk,
and a failure aborts the upload, so no partial object is ever visible.

**Why no templating?** The converter takes finished HTML; each service renders it with whatever it already uses.

**Declined libraries.** EPPlus (non-commercial licence), QuestPDF (revenue-gated), iText7 (AGPL), wkhtmltopdf
(unmaintained WebKit) and commercial engines are not used; providers ship only MIT/Apache dependencies.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Reporting packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Reporting/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
