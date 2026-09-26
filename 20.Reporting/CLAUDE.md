# 20.Reporting — Domain Brain

## What This Domain Is

Streaming generation of **structured output** — statements, regulatory extracts, bulk data exports — as CSV, Excel and
tabular PDF, plus **HTML-to-PDF** for free-form documents (invoices, letters), delivered to object storage (with a
presigned download link) or to any stream.

The word that matters is **streaming**. The failure this domain exists to prevent is a service materializing a whole
result set to build a report — fine in staging, fatal on the tenant with two million rows.

---

## Packages

```
SharedKernel.Reporting.Abstractions   → contracts, fluent definition, exporter/converter bases, delivery, telemetry, DI
SharedKernel.Reporting.Csv            → RFC 4180, hand-written, no third-party package, constant memory
SharedKernel.Reporting.Spreadsheet    → .xlsx on SpreadCheetah (streaming, constant memory, typed cells)
SharedKernel.Reporting.Pdf            → tabular PDF on PDFsharp/MigraDoc (in memory, capped by MaxRows)
SharedKernel.Reporting.Gotenberg      → IHtmlToPdfConverter over Gotenberg (headless Chromium, a Docker service)
```

`.Abstractions` + sibling providers. No provider references another; all shared behaviour (validation, delivery,
tracing, metrics, logging) lives in `.Abstractions`' base classes, so a provider only encodes bytes.

One registration chain, one namespace (`SharedKernel.Reporting`) for everything a service calls:

```csharp
builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");
builder.Services.AddSharedKernelReporting()
    .AddCsv(builder.Configuration)          // SharedKernel:Reporting:Csv
    .AddSpreadsheet(builder.Configuration)  // SharedKernel:Reporting:Spreadsheet
    .AddPdf(builder.Configuration)          // SharedKernel:Reporting:Pdf
    .AddGotenberg(builder.Configuration);   // SharedKernel:Reporting:Gotenberg — IHtmlToPdfConverter
builder.WithReportingTelemetry();           // SharedKernel.ServiceDefaults
```

Each `Add{Format}` registers **one open generic exporter for every row type** — never one registration per `TRow`.

---

## Third-Party Licensing — a ratified decision, not a default

| Library | Verdict | Reason |
|---|---|---|
| **SpreadCheetah** `1.28.0` | ✅ Adopted (spreadsheet) | MIT, no dependencies on net8.0+, streaming async writer |
| **PDFsharp** + **PDFsharp-MigraDoc** `6.2.4` | ✅ Adopted (tabular PDF) | MIT |
| **Gotenberg** `8.x` (Docker image, no NuGet) | ✅ Adopted (HTML-to-PDF) | MIT; called over HTTP, no vendor SDK |
| **Roboto** font (embedded in `.Pdf`) | ✅ Adopted | Apache-2.0 (`Fonts/LICENSE.txt`) |
| ClosedXML | ⤵ Test-only | MIT, but builds the whole workbook in memory (a 32 MB `.xlsx` cost 1+ GB, `ClosedXML#1180`) and saves synchronously. Replaced by SpreadCheetah in the 2026-09-26 pass; kept only as an independent reader in tests and `consumer-verify` |
| EPPlus | ❌ Declined | PolyForm Noncommercial |
| QuestPDF | ❌ Declined | Revenue-gated licence |
| iText7 / pdfHTML | ❌ Declined | AGPL |
| wkhtmltopdf / DinkToPdf | ❌ Declined | Abandoned, unpatched WebKit |
| IronPDF, Syncfusion | ❌ Declined | Commercial |
| PuppeteerSharp / Playwright (in-process Chromium) | Not chosen | Permissive, but puts a ~300 MB browser inside every service image and process, with its sandbox issues and exploit surface. Gotenberg isolates Chromium in its own pod. A second `IHtmlToPdfConverter` provider could still wrap one |

If a format cannot be served by an acceptably licensed dependency, **scope it out** — never ship a copyleft or
revenue-gated dependency in a published package.

**PDFsharp package IDs.** The current PDFsharp team packages are exactly two: `PDFsharp` and `PDFsharp-MigraDoc`
(which bundles `MigraDoc.DocumentObjectModel`/`MigraDoc.Rendering`). The old three-ID split no longer exists on
nuget.org; do not "fix" `Directory.Packages.props` back to it.

---

## Tiers and references

| Package | Tier | References |
|---|---|---|
| `SharedKernel.Reporting.Abstractions` | Abstractions | `SharedKernel.Primitives`, `SharedKernel.Storage.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` |
| `SharedKernel.Reporting.Csv` | Adapter | `.Abstractions`, `SharedKernel.Configuration` |
| `SharedKernel.Reporting.Spreadsheet` | Adapter | `.Abstractions`, `SharedKernel.Configuration`, `SpreadCheetah` |
| `SharedKernel.Reporting.Pdf` | Adapter | `.Abstractions`, `SharedKernel.Configuration`, `PDFsharp`, `PDFsharp-MigraDoc` |
| `SharedKernel.Reporting.Gotenberg` | Adapter | `.Abstractions`, `SharedKernel.Configuration`, `SharedKernel.Execution`, `Microsoft.Extensions.Http(.Resilience)` |

The build enforces the tiers (SKTIER001–006). `AbstractionsPurityTests` fails if `.Abstractions` references or exposes a
format library. **This domain never references a persistence package** — the caller supplies the
`IAsyncEnumerable<TRow>` (`StreamAsync`, `ListKeysetAsync`, `IFileStorage.ListAsync`, …).

**Readiness.** Only `.Gotenberg` holds a dependency worth probing: it registers the `gotenberg` probe (`GET /health`).
The exporters are stateless libraries and have none.

**Scheduling and workflows** compose in consumer code (a `19.Scheduling` job or `17.Workflows` activity calls an
exporter); no reference either way.

---

## Domain Invariants

**1 — The contract is streaming, with no escape hatch.** `IReportExporter<TRow>` takes `IAsyncEnumerable<TRow>` on both
members. **No overload taking `IEnumerable<TRow>`/`List<TRow>` may ever exist** — once it does, every caller uses it.
Per provider, honestly: CSV and Excel encode in constant memory (proven by allocation tests over 20k vs 200k–400k rows);
PDF builds a MigraDoc document in memory and is capped by `PdfExportOptions.MaxRows` (default 10,000).

**2 — Delivery streams into storage.** `ExportAsync`/`ConvertAsync` write through a `System.IO.Pipelines.Pipe` whose
reader side is `IFileStorage.UploadAsync`: bytes reach storage as they are produced. A writer that fails (a `Result`
failure such as `reporting.row_limit_exceeded`, or an exception) faults the pipe, so **a half-written report is never
stored**. An upload that stops early (failed condition, invalid key) completes the pipe's reader *and* aborts the
writer's next write, so the row source is not drained for nothing. `ExportToStreamAsync`/`ConvertToStreamAsync` write
to any stream (an HTTP response body).

**3 — Formatting is `CultureInfo`, not translation.** Headers are translated by the caller. `ReportValueFormatting`
applies a column's `Format` under the report's `Culture`; no dependency on `SharedKernel.Localization`.

**4 — PII is the caller's problem, and is said out loud.** An export is a bulk copy into a durable, shareable file.
Nothing here classifies or redacts — `01.Core/SharedKernel.DataPrivacy`, applied in the query. Spans, metrics and logs
carry format, operation, store, row count, size and error code — **never row content, object keys or file names**.

**5 — Spreadsheet-safe text.** CSV prefixes text starting with `= + - @ \t \r` with `'` (`CsvExportOptions.EscapeFormulas`,
default on; numbers are never prefixed). Excel writes text as text cells, never formulas. HTML-to-PDF: the HTML runs in
a browser — callers HTML-encode user data, and Gotenberg is deployed with an allow-list and no egress (its README).

**6 — Expected failures are `Result`s; bugs throw.** Invalid definitions/destinations, unsupported formats, row limits,
converter failures and storage failures are `Result` failures (`ReportingErrorCodes`, `storage.*`). An exception from
the row source, a value function or a formatter propagates. Missing storage registration or an unknown store throws
`InvalidOperationException` (configuration error).

---

## Design

| Concern | Choice | Notes |
|---|---|---|
| Definition | `ReportDefinition.For<T>().Title(…).Culture(…).Column(header, value, format:, alignment:, relativeWidth:).Build()` | Columns render in the order added. `ReportColumn<T>`: `Header`, `Value`, `Format`, `Formatter`, `Alignment` (`Auto` right-aligns numbers), `RelativeWidth` |
| Format metadata | `ReportFormat` (`Name`, `ContentType`, `FileExtension`; `Csv`/`Xlsx`/`Pdf`; custom formats allowed) | `WithExtension("orders")`; equality by name |
| Runtime choice | `IReportExporterFactory` — `Formats`, `ParseFormat(name / extension / content type)` → `Result`, `GetExporter<T>(format)` | Exporters are also keyed services: `[FromKeyedServices("xlsx")] IReportExporter<T>` |
| Typed injection | `ICsvReportExporter<T>`, `ISpreadsheetReportExporter<T>`, `IPdfReportExporter<T>` | Declared in each provider package |
| Extension point | `ReportExporterBase<T>` (`Format`, `EncodeAsync` → `Result<long>`, optional `ValidateDefinition`) + `builder.AddExporter(format, typeof(MyExporter<>))`; `HtmlToPdfConverterBase` (`RenderAsync`) + `AddHtmlToPdfConverter<T>()` | Both take `ReportingDependencies` (logger factory, optional `IFileStorageFactory`) |
| Delivery | `ReportDestination`: `Store`, `TenantId?`, `Key`, `DownloadFileName` (→ RFC 6266 `Content-Disposition` with UTF-8 `filename*`), `Condition` (`WriteCondition`), `Metadata`, `PresignedDownloadUrlExpiry` | Outcomes: `ReportExportOutcome` (`StoredFile`, `DownloadUrl`, `Format`, `RowCount`, `SizeBytes`), `ReportStreamOutcome`, `PdfDocumentOutcome` |
| CSV | Hand-written; `CsvExportOptions`: `Delimiter` (validated), `IncludeUtf8Bom`, `IncludeHeaderRow`, `EscapeFormulas` | Each row built in a reused `StringBuilder`, written with `StreamWriter.WriteAsync` — never a blocking write into the pipe |
| Excel | SpreadCheetah; `SpreadsheetExportOptions`: `DefaultSheetName`, `BoldHeaderRow`, `FreezeHeaderRow`, `AutoFilter`, `MaxRows` (≤ 1,048,575) | Numbers, `DateTime`/`DateTimeOffset`/`DateOnly`, `TimeOnly`, `TimeSpan` (`[h]:mm:ss`), `bool` are typed cells; .NET formats translated by `ExcelFormats` (N/F/D/P/E/C + custom numeric pass-through; date patterns); a `Formatter` column is text; title → sheet name + document title |
| PDF | PDFsharp/MigraDoc; `PdfExportOptions`: `PaperSize`, `Landscape`, `MarginMillimeters`, `FontSize`, `ShowPageNumbers`, `AlternateRowShading`, `MaxRows` | Column widths share the usable width by `RelativeWidth` (never overflow); header repeats per page; "n / N" footer; title → heading + PDF metadata. `PdfDocument.Save` needs a seekable stream, so it saves to a `MemoryStream` and copies. PDFsharp enumerates no OS fonts: Roboto is embedded via `EmbeddedRobotoFontResolver` (process-wide `GlobalFontSettings.FontResolver`) |
| HTML-to-PDF | `IHtmlToPdfConverter.ConvertAsync` / `ConvertToStreamAsync`; `HtmlToPdfOptions`: `PageSize` (mm), `Landscape`, `Margins` (mm), `PrintBackground`, `Scale`, `PreferCssPageSize`, `HeaderHtml`, `FooterHtml` (`PageNumberFooter`), `Assets` | Gotenberg `POST /forms/chromium/convert/html`, response streamed to the destination; form values in invariant culture; correlation id as `Gotenberg-Trace`; optional basic auth; `AddStandardResilienceHandler` (retries on transient errors, attempt timeout, circuit breaker sampling ≥ 2× timeout); `HttpClient.Timeout` infinite. Mapping: 4xx → `conversion_failed`, 401/403/404/429/5xx/unreachable → `converter_unavailable`, 408/504/timeout → `conversion_timeout` |
| Telemetry | `ActivitySource` + `Meter` `SharedKernel.Reporting`: spans `reporting export` / `reporting convert`; `reporting.operation.duration` (s), `reporting.rows`, `reporting.bytes`; `error.type` + error status on failure | Wired by `WithReportingTelemetry()` |
| Logging | `[LoggerMessage]`, category `SharedKernel.Reporting`, EventIds 20000–20006 (`.Abstractions` sub-block 20000–20099): started, completed, failed (error code), threw, cancelled (Debug), presign failed, converted | Sub-blocks `.Csv` 20100 / `.Spreadsheet` 20200 / `.Pdf` 20300 / `.Gotenberg` 20400 reserved, unused |
| Errors | `ReportingErrorCodes`: `invalid_definition`, `invalid_destination`, `unsupported_format`, `row_limit_exceeded`, `invalid_request`, `conversion_failed`, `converter_unavailable`, `conversion_timeout`; factories in `ReportingErrors` | Storage failures keep `storage.*` |
| Public API | `PublicAPI.Shipped/Unshipped.txt` on all five packages; implementations internal | `CS1591`/`RS0016` are errors |

---

## What Goes Where (within this domain)

| I need to add… | It belongs in… |
|---|---|
| A change to the export or conversion contract, the definition model or outcomes | `.Abstractions` |
| A change to how bytes reach storage, or to telemetry/logging | `.Abstractions` — `ReportingDependencies` (internal pipeline); never per provider |
| A format-specific encoding behaviour | The provider's `EncodeAsync` / `RenderAsync` |
| A new output format | A sibling `SharedKernel.Reporting.{Format}` (licence-checked first) deriving from `ReportExporterBase<T>`, its own `I{Format}ReportExporter<T>`, and an `Add{Format}(configuration)` calling `AddExporter` — or, inside a service, just `AddExporter(format, typeof(MyExporter<>))` |
| Another HTML-to-PDF engine | A sibling deriving from `HtmlToPdfConverterBase`, registered with `AddHtmlToPdfConverter<T>()` |
| A convenience overload taking `IEnumerable<T>`/`List<T>` | **Declined, structurally** (Invariant 1). A caller with a list uses `list.ToAsyncEnumerable()` |
| Anything that queries a database, redacts PII, translates headers or schedules | **Not here** — consumer code, `DataPrivacy`, the caller, `19.Scheduling` |
| Templating HTML (Razor, Scriban, …) | **Not here.** The converter takes a finished HTML string; the service renders it |
| An in-memory test double | `16.Testing/SharedKernel.Reporting.Testing` — `InMemoryReportExporter<T>`, `InMemoryReportExporterFactory`, `InMemoryHtmlToPdfConverter`, `AddInMemoryReporting()` |

---

## Verification

- Unit lane: `.Abstractions.Tests` (pipeline: store-nothing-on-failure, early upload stop, tenant stores, content
  disposition, spans), `.Csv.Tests`, `.Spreadsheet.Tests` (read back with ClosedXML), `.Pdf.Tests` (layout fits the page,
  read back with PDFsharp), `16.Testing/SharedKernel.Reporting.Testing.Tests`.
- Integration lane: `.Gotenberg.Tests` — stub-handler tests of every form field and error mapping, plus a real
  `gotenberg/gotenberg:8.37.0` container (Testcontainers).
- `20.Reporting/consumer-verify` — the whole chain in a real host; outputs reopened by independent readers.
- `samples/DocumentsApi` — `/reports/{store}/listing?format=` and `/pdf/{store}/{key}` against MinIO + Gotenberg, from
  the packed packages.

---

## Open Items

None.

---

## Changelog

History is in `state-map.md` ("Domain-Brain Changelog").
