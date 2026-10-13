# 20.Reporting — Domain Brain

> Streaming generation of structured output — statements, regulatory extracts, bulk exports — as CSV, Excel and
> tabular PDF, plus HTML-to-PDF for free-form documents (invoices, letters), delivered to a `08.Storage` store (with a
> presigned download link) or to any stream. The word that matters is **streaming**: this domain exists to stop a
> service materializing a whole result set to build a report. It deliberately does not query databases, redact PII,
> translate headers, template HTML or schedule jobs — the caller supplies the `IAsyncEnumerable<TRow>`, the finished
> HTML string and the translated headers.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Reporting.Abstractions` | Abstractions | Contracts, fluent `ReportDefinition`, `ReportExporterBase<T>`/`HtmlToPdfConverterBase`, the storage-delivery pipeline, telemetry, logging, DI. References `SharedKernel.Primitives`, `SharedKernel.Core` (`SharedKernelException`), `SharedKernel.Storage.Abstractions` |
| `SharedKernel.Reporting.Csv` | Adapter | RFC 4180, hand-written, no third-party package, constant memory |
| `SharedKernel.Reporting.Spreadsheet` | Adapter | `.xlsx` on SpreadCheetah (MIT) — streaming, constant memory, typed cells |
| `SharedKernel.Reporting.Pdf` | Adapter | Tabular PDF on PDFsharp + PDFsharp-MigraDoc (MIT) — in memory, capped by `MaxRows`; embedded Roboto (Apache-2.0) |
| `SharedKernel.Reporting.Gotenberg` | Adapter | `IHtmlToPdfConverter` over a Gotenberg container (headless Chromium) via `Microsoft.Extensions.Http.Resilience`; references `SharedKernel.Execution` for the correlation id |
| `consumer-verify` | — (not packable) | The whole chain in a real host; outputs reopened by independent readers |

Providers are siblings: none references another. All adapters also reference `SharedKernel.Configuration`.

## Public Entry Points

One namespace (`SharedKernel.Reporting`) and one chain:

```csharp
builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");
builder.Services.AddSharedKernelReporting()           // IReportingBuilder, IReportExporterFactory
    .AddCsv(builder.Configuration)                    // SharedKernel:Reporting:Csv          → CsvExportOptions
    .AddSpreadsheet(builder.Configuration)            // SharedKernel:Reporting:Spreadsheet  → SpreadsheetExportOptions
    .AddPdf(builder.Configuration)                    // SharedKernel:Reporting:Pdf          → PdfExportOptions
    .AddGotenberg(builder.Configuration);             // SharedKernel:Reporting:Gotenberg    → GotenbergOptions
// Spans and instruments (SharedKernel.Reporting) are subscribed by the SharedKernel.* wildcard of AddServiceDefaults.
```

Key types (members, options and defaults: the `SharedKernel.Reporting.*` READMEs):

- `ReportDefinition.For<T>()…Build()` — the column model; `ReportFormat` (`Csv`/`Xlsx`/`Pdf`); `ReportDestination`
  (named or tenant store, key, download file name, presigned-link expiry).
- `IReportExporter<TRow>` — `ExportAsync` (to a `ReportDestination`) / `ExportToStreamAsync`; also keyed by format
  (`[FromKeyedServices("xlsx")]`) and typed per provider (`ICsvReportExporter<T>`, `ISpreadsheetReportExporter<T>`,
  `IPdfReportExporter<T>`).
- `IReportExporterFactory` — runtime format choice (`ParseFormat` → `Result`, `GetExporter<T>`).
- `IHtmlToPdfConverter` — `ConvertAsync` / `ConvertToStreamAsync`.
- Extension points: `ReportExporterBase<T>` + `AddExporter(format, typeof(MyExporter<>))`; `HtmlToPdfConverterBase` +
  `AddHtmlToPdfConverter<T>()`.
- `ReportDefinitionBuilder.Build()` throws `InvalidReportDefinitionException` (`reporting.invalid_definition`, `Validation`).
- `ReportingDiagnostics` — `ActivitySourceName` / `MeterName` (`SharedKernel.Reporting`) for subscribing and asserting.
- `.Gotenberg` registers the `gotenberg` readiness probe (`GET /health`).

## Rules & Invariants

1. **The contract is streaming, with no escape hatch.** `IReportExporter<TRow>` takes `IAsyncEnumerable<TRow>`. Never add
   an `IEnumerable<TRow>`/`List<TRow>` overload — once it exists every caller uses it. A caller with a list uses
   `ToAsyncEnumerable()`.
2. CSV and Excel encode in constant memory (proven by allocation tests over 20k vs 200k–400k rows). PDF builds a MigraDoc
   document in memory and must stay capped by `PdfExportOptions.MaxRows` (→ `reporting.row_limit_exceeded`).
3. **A half-written report is never stored.** `ExportAsync`/`ConvertAsync` write through a `System.IO.Pipelines.Pipe`
   whose reader is `IFileStorage.UploadAsync`. A writer failure (a `Result` failure or an exception) faults the pipe. An
   upload that stops early completes the reader and aborts the writer's next write, so the row source is not drained for
   nothing.
4. Delivery, validation, tracing, metrics and logging live once in `.Abstractions` (`ReportingDependencies`, the base
   classes). A provider only encodes bytes (`EncodeAsync` → `Result<long>`, `RenderAsync`).
5. Each `Add{Format}` registers **one open-generic exporter for every row type** — never one registration per `TRow`.
6. Formatting is `CultureInfo`, not translation: `ReportValueFormatting` applies a column's `Format` under the report's
   `Culture`. No dependency on `SharedKernel.Localization`.
7. Telemetry and logs carry format, operation, store, row count, size and error code — **never row content, object keys
   or file names**. Nothing here classifies or redacts; PII is filtered in the caller's query (`SharedKernel.DataPrivacy`).
8. Spreadsheet-safe text: CSV prefixes text starting with `= + - @ \t \r` with `'` when `EscapeFormulas` (default on;
   numbers never prefixed). Excel writes text as text cells, never formulas. HTML-to-PDF callers HTML-encode user data.
9. Expected failures are `Result`s (`ReportingErrorCodes`: `invalid_definition`, `invalid_destination`,
   `format_not_supported`, `row_limit_exceeded`, `invalid_request`, `conversion_failed`, `converter_unavailable`,
   `conversion_timeout`; storage failures keep `storage.*`; a converter error is a fixed sentence per code, rule 15).
   Exceptions from the row source, a value function or a formatter propagate. Configuration errors (missing storage
   registration, unknown store, unregistered format, duplicate exporter, missing font resource) throw
   `InvalidOperationException` with a sentence that names the remedy (E2, E3).
10. `.Abstractions` never references or exposes a format library (`AbstractionsPurityTests`), and this domain never
    references a persistence package.
11. Gotenberg error mapping: 4xx → `conversion_failed`; 401/403/404/429/5xx/unreachable → `converter_unavailable`;
    408/504/timeout → `conversion_timeout`. `HttpClient.Timeout` is infinite — the resilience handler owns timeouts
    (attempt timeout = `Timeout`, circuit-breaker sampling ≥ 2× timeout). Form values are written in invariant culture;
    the correlation id goes out as `Gotenberg-Trace` (rule 19).
12. Only `.Gotenberg` registers a readiness probe; exporters are stateless libraries and have none.
13. Every public member is in `PublicAPI.Shipped/Unshipped.txt` (`RS0016` is an error); implementations stay internal.
14. Never ship a copyleft or revenue-gated dependency; if no acceptably licensed library serves a format, scope it out.
15. **No provider detail in an `Error`.** A converter's `Error` is a fixed sentence per code: never the converter's host, route, credential hint, status text or response body (E3, L5, rule 7). The HTTP status code and `HttpRequestError` go to a log record in the `.Gotenberg` sub-block (20400-20401). `ReportingErrors.ConverterUnavailable()`/`ConversionFailed()` take no free-text reason.
16. An exception that carries an `Error` derives `SharedKernelException` (ctors `(Error)`, `(Error, Exception)`): `ReportAbortedException` (internal) carries the writer's own `Error`, so a span or edge that sees it classifies the real cause.
17. **Observability gold standard.** Failures on `reporting export`/`reporting convert` are recorded only by the shared Primitives recorder (`error.type` is an `ErrorType` or exception full name, `error.code` is the code, cancellation is never an error); instruments are `sharedkernel.reporting.*` with units and an `outcome` tag, and the metric's `error.type` is bounded (never an error code); the `Meter` comes from `IMeterFactory` when registered and is created directly otherwise; logs follow L1-L8 (category `ILogger<ReportingDependencies>`, required logger, sentence templates, per-operation success at Debug).
18. `ReportingErrors` members are public only when a sibling assembly (a provider or the `.Testing` double) constructs them; `InvalidDefinition` and `InvalidRequest` are internal (`ErrorShapeTests` pins the set). `ReportDefinitionBuilder.Build()` throws `InvalidReportDefinitionException : SharedKernelException` (`reporting.invalid_definition`, `Validation`); the other `InvalidOperationException`s are E2 configuration errors with E3 wording.
19. The Gotenberg correlation id is written through `RequestContextPropagation.WriteHeaders` with a null context and an explicit id (mapped to `Gotenberg-Trace`); no tenant, actor or client header is sent to the converter, and an invalid or absent id sends no header.

## Decisions

| Decision | Why |
| --- | --- |
| SpreadCheetah for `.xlsx` | MIT, dependency-free, streaming async writer |
| ClosedXML test-only | MIT but builds the whole workbook in memory and saves synchronously; kept as an independent reader in tests and `consumer-verify` |
| PDFsharp + PDFsharp-MigraDoc for tabular PDF | MIT. These are the only two current package IDs — do not "fix" `Directory.Packages.props` back to an older three-ID split |
| Roboto embedded via `EmbeddedRobotoFontResolver` (process-wide `GlobalFontSettings.FontResolver`) | PDFsharp enumerates no OS fonts |
| PDF saves to a `MemoryStream` then copies | `PdfDocument.Save` needs a seekable stream |
| Gotenberg (a separate container) for HTML-to-PDF | Isolates Chromium in its own pod; no vendor SDK, called over HTTP |
| Declined EPPlus, QuestPDF, iText7/pdfHTML, wkhtmltopdf/DinkToPdf, IronPDF, Syncfusion | PolyForm Noncommercial, revenue-gated, AGPL, abandoned/unpatched WebKit, commercial |
| In-process PuppeteerSharp/Playwright not chosen | Puts a ~300 MB browser and its exploit surface in every service; could still be a second `IHtmlToPdfConverter` provider |
| No HTML templating (Razor, Scriban) | The converter takes finished HTML; the service renders it |
| Runtime format choice through `IReportExporterFactory` plus keyed services | A user-chosen `?format=` without a switch statement in every service |
| `DownloadFileName` becomes an RFC 6266 `Content-Disposition` with UTF-8 `filename*` | Non-ASCII file names survive presigned downloads |
| Gotenberg errors carry no host, route or body; the status code is logged instead | Engine output may echo document content (personal data) and a base URL is internal topology; an `Error` reaches API clients through ProblemDetails. Rejected: a closed `reason` enum on the factories, a truncated body |
| `Meter` from an optional `IMeterFactory`, direct `Meter` as fallback | `AddMetrics()` is in `Microsoft.Extensions.Diagnostics`, not an `.Abstractions` package, so Abstractions tier cannot register it; Host builders already do |
| `reporting.unsupported_format` renamed `reporting.format_not_supported` | C3 reason word, same family as `search.not_supported` / `intelligence.not_supported` |
| `Build()` failures are a classified `Validation` exception, not `InvalidOperationException` | Column lists are often chosen at runtime and the same condition already returns `reporting.invalid_definition` from `ExportAsync`; a defect-style 500 would hide the code |
| Gotenberg keeps the vendor header `Gotenberg-Trace` but the value passes `RequestContextPropagation.WriteHeaders` | The only header Gotenberg correlates its logs with; the shared helper owns the validated id; nothing but the correlation id leaves the platform |

## Logging

Block **20000–20999** (`LoggingEventIdRanges.Reporting`), category `SharedKernel.Reporting.ReportingDependencies`
(`ILogger<ReportingDependencies>`, a required dependency).

| Sub-block | Package | In use |
| --- | --- | --- |
| 20000–20099 | `.Abstractions` (`Diagnostics/ReportingLog.cs`, ids in internal `ReportingLoggingEventIds`) | 20000–20006: started (Debug), completed (Debug), failed (Warning; error code and type), threw (Error), cancelled (Debug), presign failed (Warning), converted (Debug) |
| 20100–20199 | `.Csv` | reserved, unused |
| 20200–20299 | `.Spreadsheet` | reserved, unused |
| 20300–20399 | `.Pdf` | reserved, unused |
| 20400–20499 | `.Gotenberg` | 20400 rejected (Warning, HTTP status code), 20401 unreachable (Warning, `HttpRequestError`, exception attached) via internal `GotenbergLoggingEventIds`/`GotenbergLog` |

EventIds are written as `LoggingEventIdRanges.Reporting + n` through the package's `{Package}LoggingEventIds`. Templates
are one sentence with quoted identifiers; no template names a store key, file name, host or row value.

Telemetry: `ActivitySource`/`Meter` `SharedKernel.Reporting` (`ReportingDiagnostics`, subscribed by the
`SharedKernel.*` wildcard) — spans `reporting export`/`reporting convert` (Internal); instruments
`sharedkernel.reporting.operation.duration` (s; tags `reporting.format`, `reporting.operation`, `outcome`, `error.type`
on failure and exception), `sharedkernel.reporting.rows` ({row}), `sharedkernel.reporting.bytes` (By). Package-local tag
keys are in internal `ReportingTagKeys`.

## Cross-Domain Couplings

| Domain | Seam |
| --- | --- |
| `08.Storage` | `.Abstractions` references `Storage.Abstractions`; delivery is `IFileStorage.UploadAsync` on a named or tenant store (`ReportDestination.TenantId`), `WriteCondition`, presigned download URLs |
| `01.Core` | `Primitives` (`Result`, `Error`, `LoggingEventIdRanges`, `IReadinessProbe`), `Configuration` (`AddValidatedOptions`, `ISectionBoundOptions`), `Execution` (Gotenberg reads the correlation id) |
| `06.Persistence` | None by reference; callers compose `StreamAsync`/`ListKeysetAsync` into the row source |
| `13.ServiceDefaults` | The `SharedKernel.*` wildcard subscribes the `SharedKernel.Reporting` source and meter (no `WithReportingTelemetry()`); `AddSharedKernelReadiness()` maps the `gotenberg` probe |
| `14.Presentation` | The edge mappers answer `InvalidReportDefinitionException` and `ReportAbortedException` (both `SharedKernelException`s) from their `Error`; their status assertions live there |
| `17.Workflows`, `19.Scheduling` | Compose in consumer code (a job or activity calls an exporter); no reference either way |

## Testing

- **Unit** lane: `.Abstractions.Tests` (spans and instruments through `SharedKernel.Testing` recorders, log records pinned per template, error-code table, `InvalidReportDefinitionException`, host start with and without `AddMetrics()`; test classes run serially because the source and meter are process-wide; pipeline: nothing stored on failure, early upload stop, tenant stores,
  content disposition, spans, `ReportAbortedException` reaching the store with the writer's `Error`, `AbstractionsPurityTests`), `.Csv.Tests`, `.Spreadsheet.Tests` (read back with
  ClosedXML), `.Pdf.Tests` (layout fits the page, read back with PDFsharp), `consumer-verify`,
  `SharedKernel.Reporting.Testing.Tests`.
- **Integration** lane: `.Gotenberg.Tests` — stub-handler tests of every form field and error mapping (a sentinel matrix proves no host, body or reason phrase reaches an `Error` or a log record; log capture through `SharedKernel.Testing`), plus a real
  container through the suite's own `GotenbergFixture` (pinned `gotenberg/gotenberg:8.37.0`, in `GotenbergContainerTests.cs`).
- Fakes: `SharedKernel.Reporting.Testing` — catalogue in `src/Testing/CLAUDE.md`.
- End to end: the Shop's Reports (`samples/Shop/Reports`: `POST /reports/sales?format=&store=`, `POST /reports/sales/statement`) against MinIO + Gotenberg (`Shop.E2E` `ReportsFlowTests`).

## Known Limitations

- PDF export is not constant-memory; it is capped by `MaxRows` (default 10,000).
- Excel is capped at 1,048,575 data rows per sheet (the format's limit).
- HTML-to-PDF needs a Gotenberg deployment, which must be locked down (allow-list, no egress) — see its README.
