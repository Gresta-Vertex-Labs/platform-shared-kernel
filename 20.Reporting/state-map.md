# 20.Reporting — State Map

> **What this file is:** Phase and task tracker for all work within `20.Reporting`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.20.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition | Root Backlog ID |
| --- | --- | --- | --- |
| `SK.20.Design` | Design | All tasks in Phase: Design are `●` | — |
| `SK.20.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` | — |
| `SK.20.Core` | Core | All tasks in Phase: Core are `●` | — |
| `SK.20.Tests` | Tests | All tasks in Phase: Tests are `●` | — |
| `SK.20.Docs` | Docs | All tasks in Phase: Docs are `●` | — |
| `SK.20.Published` | Published | All tasks in Phase: Published are `●` | — |

> **Root Backlog ID column:** left `—` on every lifecycle row deliberately. P-477–P-480 each span Design→Published, so attaching them to a single lifecycle key would close them prematurely (see `/state-map-phase` Step S8a, Case 3). They close via Step S8c when every phase key is `●`.

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IReportExporter<TRow> | SK.20.Core | SharedKernel.Reporting.Abstractions | ◐ |
-->

---

## Blocked

_Nothing blocked._

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Reporting.Abstractions` | Published | `●` | P-477/WO-077; rebuilt by the 2026-09-26 pre-publish pass. One namespace `SharedKernel.Reporting`: `IReportExporter<TRow>` (+ `Format`), `IReportExporterFactory`, `IHtmlToPdfConverter`, `ReportFormat`, fluent `ReportDefinition.For<T>()`, `ReportExporterBase<T>`/`HtmlToPdfConverterBase`, `ReportingDependencies` (internal pipe delivery, telemetry, logging), `AddSharedKernelReporting()`/`AddExporter`, `ReportingErrorCodes`. PublicAPI tracked. 60 tests. |
| `SharedKernel.Reporting.Csv` | Published | `●` | P-478; formula-injection guard, `ISectionBoundOptions` options with delimiter validation, open-generic registration (`AddCsv`). Constant memory proven (20k vs 400k rows). 22 tests. |
| `SharedKernel.Reporting.Spreadsheet` | Published | `●` | P-479; **moved from ClosedXML to SpreadCheetah 1.28.0** (MIT) — streaming, async, constant memory; typed cells with translated number formats; frozen bold header, autofilter, widths, `MaxRows`. 25 tests (read back with ClosedXML). |
| `SharedKernel.Reporting.Pdf` | Published | `●` | P-480; PDFsharp/MigraDoc 6.2.4. Fixed column overflow (widths share the page by `RelativeWidth`), page numbers, title metadata, numeric right-alignment, shading, `PaperSize`/`Landscape`/`MarginMillimeters`/`FontSize`/`MaxRows`. 8 tests. |
| `SharedKernel.Reporting.Gotenberg` | Published | `●` | **New (2026-09-26).** `IHtmlToPdfConverter` over Gotenberg 8 (Chromium in its own container), streamed to storage or a stream; resilience handler, `Gotenberg-Trace`, basic auth, `gotenberg` probe. 18 tests (Integration lane, real container). |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.20.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Result`, `Error`) | Available |
| `SK.20.Scaffold` | `01.Core` | `SharedKernel.Configuration` ProjectReference (`AddValidatedOptions`) — provider packages only | Available |
| `SK.20.Scaffold` | `08.Storage` | `SharedKernel.Storage.Abstractions` ProjectReference — `IFileStorage`, `IBlobUriGenerator`, `FileUploadRequest`/`PresignedUrlRequest` for the delivery path (verified signatures on disk 2026-08-26 — `IFileStorage.UploadAsync` reads `FileUploadRequest.Content` as a plain caller-owned `Stream` from its current position, no upfront `Content-Length` required, which is exactly what a `Pipe`-fed streaming upload needs) | Available |
| `SK.20.Scaffold` | — (`devops-lead` territory, outside this domain's jurisdiction) | Root `Directory.Packages.props` pins for **`ClosedXML`** and for the PdfSharp/MigraDoc pairing. | **Resolved 2026-09-04**, performed by the phase-implementer per explicit dispatch authorization overriding the "devops-lead territory" note. Pinned `ClosedXML` `0.105.1`. **Correction to the original plan, verified against the live NuGet index, not assumed:** the three-way `PdfSharp` + `MigraDoc.DocumentObjectModel` + `MigraDoc.Rendering` package split no longer exists on nuget.org. The current, actively-maintained PDFsharp-team packages are exactly two IDs — `PDFsharp` `6.2.4` (core) and `PDFsharp-MigraDoc` `6.2.4` (bundles the MigraDoc namespaces this design already named, depends on `PDFsharp` at the identical version) — both unconditional MIT, verified directly against each published nuspec before pinning. This is a corrected package-ID mapping onto the same licence-ratified technology, never a substitution. All four `<PackageVersion>` entries now present in root `Directory.Packages.props`, adjacent to their alphabetical neighbors, each with a comment explaining the pin. |
| `SK.20.Core` | `01.Core` | `EventId` range registry entry — domain base `20000`–`20999`, sub-blocks per package in declaration order: `.Abstractions` 20000-20099, `.Csv` 20100-20199, `.Spreadsheet` 20200-20299, `.Pdf` 20300-20399 (D-11) | **Shipped — verified directly, not assumed, contradicting this row's prior "design-locked, implementation pending" status.** Read `01.Core/SharedKernel.Primitives/Logging/LoggingEventIdRanges.cs` in full 2026-09-04: `public const int Reporting = 20000;` is present in code, immediately after `Scheduling = 19000`. `01.Core` shipped this entry between the 2026-08-26 dispatch note and this session. `[LoggerMessage]` entries were authored against this range without any workaround — C-02's `ReportingLog` (20000-20002) is real. C-09/C-13/C-17 (per-provider additional entries) were evaluated and deliberately left empty — `StorageStreamingWriter`'s three entries (export-started/completed/failed) already cover every provider generically; no provider-specific event was found to need its own entry, a decision recorded rather than an oversight |
| `SK.20.Core` | `06.Persistence` | Nothing at compile time. This domain **never** references persistence — the caller supplies the `IAsyncEnumerable<TRow>`. Listed here only to record that the absence is deliberate | N/A by design |

> **No `13.ServiceDefaults` readiness-probe grant exists or is needed for this domain.** Unlike `06.Persistence`/`08.Storage`/`09.Search`/`10.Intelligence`/`17.Workflows`/`19.Scheduling`, this domain holds no persistent connection that can be ready or not ready — it is a stateless library in the same class as `01.Core.Compression`/`.Cryptography`. Recorded explicitly so a future session does not read the absence as an oversight (P-477 acceptance criterion).

---

## Phase: Design <!-- phase-key: SK.20.Design -->

> Lock the `IReportExporter<TRow>` contract, the column/field-definition model, the culture-formatting seam, the storage-delivery composition, and each provider's encoding strategy before any implementation begins. Covers P-477 (Abstractions), P-478 (`.Csv`), P-479 (`.Spreadsheet`), P-480 (`.Pdf`) — the three providers depend on P-477's contract being locked first.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | **Lock `IReportExporter<TRow>` shape — two members, not one.** `Task<Result<ReportExportOutcome>> ExportAsync(IAsyncEnumerable<TRow> rows, ReportDefinition<TRow> definition, ReportDestination destination, CancellationToken ct)` is the primary, `IFileStorage`-delivered path. `Task<Result> ExportToStreamAsync(IAsyncEnumerable<TRow> rows, ReportDefinition<TRow> definition, Stream destination, CancellationToken ct)` is the Invariant-2-sanctioned small-output/direct-stream convenience path — still accepts `IAsyncEnumerable<TRow>` (never `IEnumerable`/`List<TRow>`), so it does not touch Invariant 1. Each concrete provider implements `ExportToStreamAsync` once as its real encoding logic; `ExportAsync` composes it with storage delivery (D-03) rather than duplicating the encoder. `IFileStorage`/`IBlobUriGenerator` are constructor-injected into each concrete exporter class, never passed as per-call parameters — matches how every other domain's dispatcher/client receives its infrastructure dependency. | SharedKernel.Reporting.Abstractions | `●` |
| D-02 | **Column/field-definition model.** `ReportColumn<TRow>` (`Header` string, `Ordinal` int — explicit, not implicit array position, so columns may be declared out of output order — `ValueSelector: Func<TRow, object?>`, optional `Formatter: Func<object?, CultureInfo, string?>`). `ReportDefinition<TRow>` (`Columns: IReadOnlyList<ReportColumn<TRow>>`, `Culture: CultureInfo` defaulting to `CultureInfo.InvariantCulture`, optional `Title` a provider may use for a sheet name/document title, never mandatory). A `Formatter` returning `null` and a `ValueSelector` returning `null` both mean "blank cell" — no provider ever writes the literal string `"null"`. | SharedKernel.Reporting.Abstractions | `●` |
| D-03 | **Default value-formatting helper.** A static `ReportValueFormatting.Format(object? value, CultureInfo culture)` used by every provider when a column supplies no `Formatter`: `IFormattable` values format via `culture` (correct for numbers/dates/currency — the BCL capability Invariant 3 is about), `bool` formats as `"True"`/`"False"` unless overridden, everything else via `value.ToString()`, `null` → `null` (blank). Lives in `.Abstractions` so all three providers apply the identical default rather than three subtly different ones. | SharedKernel.Reporting.Abstractions | `●` |
| D-04 | **Delivery/outcome model.** `ReportDestination` (`Bucket`, `Key`, optional `Metadata`, optional `PresignedDownloadUrlExpiry` — `null` means no presigned URL is requested even if an `IBlobUriGenerator` is available). `ReportExportOutcome` (`FileReference StoredFile`, `PresignedUrl? DownloadUrl`, `long RowCount` — counted for free while enumerating, valuable for export-completion telemetry/audit without adding a second pass over the row source). | SharedKernel.Reporting.Abstractions | `●` |
| D-05 | **`StorageStreamingWriter` — the concrete mechanism behind Invariant 2.** A shared internal-but-testable helper in `.Abstractions`, built on `System.IO.Pipelines.Pipe` (BCL, not third-party): opens a `Pipe`, starts `IFileStorage.UploadAsync` concurrently reading `FileUploadRequest.Content = pipe.Reader.AsStream()`, while the caller-supplied encoding delegate (`Func<Stream, CancellationToken, Task>` — each provider's `ExportToStreamAsync` body) writes into `pipe.Writer.AsStream()`; `Task.WhenAll` on both; the writer side completes the pipe on success or propagates the fault to the reader side on failure so neither task hangs. This is what lets `ExportAsync` never buffer the full output as a byte array before upload — bytes flow to storage as they are encoded, bounded by the pipe's own (small, configurable) internal buffer, regardless of what a given provider's row-to-bytes encoding step itself does or does not buffer (see D-08/D-10 for where that per-provider distinction matters). | SharedKernel.Reporting.Abstractions | `●` |
| D-06 | **CSV encoding rules (RFC 4180).** Delimiter `,`; a field is quoted when it contains `,`, `"`, `\r`, or `\n`; an embedded `"` is escaped by doubling it; line terminator `\r\n` per the RFC. This escaping rule is applied uniformly to every formatted string regardless of source type — a culture whose decimal separator is `,` (e.g. `de-DE`) produces a numeric field that already contains a comma, and the same general "contains a comma → quote it" rule handles it correctly with no numeric-specific special case. `CsvExportOptions.IncludeUtf8Bom` defaults to `true` — the target audience (statements/regulatory extracts opened by a business user in Excel) benefits from the BOM far more often than it's harmed by it; RFC 4180 itself is silent on BOM, so this is a deliberate platform default, not a spec requirement. Encoding happens directly against the destination `Stream` via `StreamWriter`, one row at a time, as `IAsyncEnumerable<TRow>` is enumerated — genuinely O(1) memory relative to row count, the only one of the three providers that can make that claim end to end. | SharedKernel.Reporting.Csv | `●` |
| D-07 | **`.Spreadsheet` write strategy.** `SpreadsheetReportExporter<TRow>` opens one `XLWorkbook`, adds one `IXLWorksheet`, writes the header row from `ReportDefinition<TRow>.Columns` ordered by `Ordinal`, then appends one row per streamed `TRow` directly into worksheet cells as `IAsyncEnumerable<TRow>` is enumerated — deliberately never buffers rows into an intermediate `List<TRow>` first, which would double the memory cost for no benefit. `ClosedXML` has no async `SaveAs` overload in any version this platform would pin — `workbook.SaveAs(stream)` is a synchronous, blocking call, called directly inside the exporter's async method body. This is accepted because report generation is expected to run inside a background/worker context (a `19.Scheduling` job or `17.Workflows` activity per the domain's own composition story), never on a request thread, so a blocking call inside it costs nothing a request-thread caller would feel. | SharedKernel.Reporting.Spreadsheet | `●` |
| D-08 | **ClosedXML's real memory behavior — verified, not assumed, and documented plainly.** Web research (2026-08-26) confirms `ClosedXML` exposes **no incremental/streaming write path**: `XLWorkbook.SaveAs` builds the complete in-memory workbook object graph and only serializes to the destination stream at `SaveAs` time; a reported real-world case saw a 32 MB `.xlsx` output consume 1+ GB of process memory mid-save (`ClosedXML/ClosedXML#1180`). D-07's "no `List<TRow>` buffering" discipline avoids doubling that cost, but it does **not** make this provider memory-bounded — the workbook's own cell object graph is O(rows × columns) regardless. **This is recorded as a known, permanent limitation of the ClosedXML dependency, not a defect to "fix" in a later phase** — no evaluated MIT-licensed alternative exposes true streaming `.xlsx` writes (the SAX-style `DocumentFormat.OpenXml` primitive does, but at a much lower ergonomic/implementation-risk level the P-479 root definition already weighed and declined in favor of ClosedXML). XML docs (DO-04) and the package README (DO-05) must state this **in capitals**, and must point a caller with a genuinely huge row count at `SharedKernel.Reporting.Csv` instead. | SharedKernel.Reporting.Spreadsheet | `●` |
| D-09 | **`.Pdf` write strategy and scope boundary.** `PdfReportExporter<TRow>` builds one `MigraDoc.DocumentObjectModel.Document`, adds a single `Table` with a header `Row` plus one `Row` per streamed `TRow`, then renders via `MigraDoc.Rendering.PdfDocumentRenderer` to a `PdfSharp` `PdfDocument` saved to the destination stream. MigraDoc's own table-rendering engine handles pagination/page-break placement automatically once a table exceeds one page — this is the mechanism T-xx's multi-page acceptance test proves, not anything this package implements itself. Explicitly out of scope, stated in XML docs in capitals: multi-section documents, images, charts, headers/footers beyond a single optional title — this provider is for one flat statement-style table, nothing else. | SharedKernel.Reporting.Pdf | `●` |
| D-10 | **`.Pdf`'s memory model, and why the scope boundary is the mitigation.** Same underlying constraint as D-08 — MigraDoc's `Document`/`Table` object model materializes fully in memory before `PdfDocumentRenderer` runs, and `PdfSharp` has no incremental page-flush API either. This is **not separately re-litigated as a defect** because P-480's scope (D-09) already bounds it: a "simple tabular/statement layout" is not the bulk-export use case CSV exists for, so the row counts this provider is meant to see are inherently small enough that the constraint rarely matters in practice. XML docs (DO-07) must still name the constraint honestly rather than imply streaming, and must point a caller who actually needs a huge exported table at `.Csv` (or a spreadsheet, with D-08's caveat) instead of `.Pdf`. | SharedKernel.Reporting.Pdf | `●` |
| D-11 | **Cross-cutting telemetry and logging plan.** `ActivitySource("SharedKernel.Reporting")` in `.Abstractions`, one span per `ExportAsync`/`ExportToStreamAsync` call, tag keys held in a local `ReportingTagKeys` constants class (format name, row count, destination bucket — never a raw string literal at an `Activity.SetTag` call site, per SK0022). `[LoggerMessage]` entries in `StorageStreamingWriter` for export-started (Debug), export-completed (Information: `RowCount`, `Duration`, never row content), export-failed (Warning, exception). EventId sub-block plan locked as `.Abstractions` 20000-20099 / `.Csv` 20100-20199 / `.Spreadsheet` 20200-20299 / `.Pdf` 20300-20399 (declaration order) — **blocked in code** until `01.Core` ships `Reporting = 20000` (see Cross-Domain Dependencies). | All four | `●` |
| D-12 | **Scope-boundary XML-doc commitments (P-477 AC #4/#5).** Lock the exact cross-reference wording used at every "not here" boundary so all four packages state it identically: tenant provisioning → `13.ServiceDefaults`; data classification/redaction/PII masking → `01.Core/SharedKernel.DataPrivacy` (WO-076); running an export on a schedule → `19.Scheduling`/`17.Workflows` (composition lives in consumer code, no grant needed either direction); no readiness probe exists or is needed, by design. The PII statement (Invariant 5) must be stated on `IReportExporter<TRow>` itself, in capitals, not only in `CLAUDE.md` — a type-level doc comment is the one place every consumer is guaranteed to see it. | All four | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.20.Scaffold -->

> Wire up `.csproj` references, the required `Directory.Packages.props` pins (blocked, see Cross-Domain Dependencies), folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `SharedKernel.Reporting.Abstractions.csproj` (`net10.0`); `ProjectReference`s to `SharedKernel.Primitives`, `SharedKernel.Storage.Abstractions`; folder skeleton `Exporters/`, `Models/`, `Formatting/`, `Delivery/`, `Diagnostics/`. Confirm zero `PackageReference` beyond BCL (`System.IO.Pipelines` ships in the shared framework on `net10.0`, no NuGet reference needed). | SharedKernel.Reporting.Abstractions | `●` |
| S-02 | Create `SharedKernel.Reporting.Csv.csproj` (`net10.0`); `ProjectReference` to `SharedKernel.Reporting.Abstractions` only; folder skeleton `Exporters/`, `Options/`, `Extensions/`. | SharedKernel.Reporting.Csv | `●` |
| S-03 | Create `SharedKernel.Reporting.Spreadsheet.csproj` (`net10.0`); `ProjectReference` to `SharedKernel.Reporting.Abstractions`; `<PackageReference Include="ClosedXML" />` (unversioned, CPM-style) — **blocked** until the root `Directory.Packages.props` `ClosedXML` pin lands (see Cross-Domain Dependencies). | SharedKernel.Reporting.Spreadsheet | `●` |
| S-04 | Create `SharedKernel.Reporting.Pdf.csproj` (`net10.0`); `ProjectReference` to `SharedKernel.Reporting.Abstractions`; `<PackageReference Include="PdfSharp" />` + `<PackageReference Include="MigraDoc.DocumentObjectModel" />` + `<PackageReference Include="MigraDoc.Rendering" />` (unversioned, CPM-style) — **blocked** until the root `Directory.Packages.props` pins for all three land (see Cross-Domain Dependencies). | SharedKernel.Reporting.Pdf | `●` |
| S-05 | Register all four projects and a `20.Reporting` solution folder in `Platform.SharedKernel.slnx`. | All four | `●` |
| S-06 | Scaffold empty `.Tests` project stubs (classlib, `net10.0`, referencing `SharedKernel.Testing`) nested inside each package's own folder — `SharedKernel.Reporting.Abstractions.Tests`, `.Csv.Tests`, `.Spreadsheet.Tests`, `.Pdf.Tests` — no test logic yet. | All four | `●` |
| S-07 | NuGet packaging metadata skeleton on all four `.csproj`s (`PackageId`, `Description`, `PackageTags`) per `Directory.Build.props` convention; no `<Version>`/`<VersionPrefix>` element on any (root Package Versioning rule). | All four | `●` |

---

## Phase: Core <!-- phase-key: SK.20.Core -->

> Implement the abstraction and all three format providers against the contract locked in Design.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `IReportExporter<TRow>`, `ReportColumn<TRow>`, `ReportDefinition<TRow>`, `ReportDestination`, `ReportExportOutcome`, `ReportValueFormatting` per D-01–D-04. | SharedKernel.Reporting.Abstractions | `●` |
| C-02 | Implement `StorageStreamingWriter` (`Pipe`-based concurrent encode+upload) per D-05, including fault propagation from the writer side into the reader-side `UploadAsync` task and vice versa so neither task can hang on the other's failure. `[LoggerMessage]` entries per D-11 — **blocked on `01.Core` shipping `Reporting = 20000`** (see Cross-Domain Dependencies). | SharedKernel.Reporting.Abstractions | `●` |
| C-03 | Implement `ReportingErrors` (a `StorageErrors`-shaped static `Error` factory class) for this domain's own failure cases — e.g. an empty `ReportDefinition<TRow>.Columns`, a destination missing `Bucket`/`Key` — distinct from and never duplicating `08.Storage.Abstractions.Errors.StorageErrors`, which `StorageStreamingWriter` surfaces unchanged for genuine storage-layer failures. | SharedKernel.Reporting.Abstractions | `●` |
| C-04 | Implement `ActivitySource("SharedKernel.Reporting")` + `ReportingTagKeys` per D-11. | SharedKernel.Reporting.Abstractions | `●` |
| C-05 | Implement `ICsvReportExporter<TRow> : IReportExporter<TRow>` — the provider-exclusive marker interface, declared only in this package, mirroring the compile-time-enforced provider-exclusivity pattern `09.Search`/`10.Intelligence` already established (referencing it against a `.Spreadsheet`/`.Pdf`-only composition root fails to compile, never a runtime check). | SharedKernel.Reporting.Csv | `●` |
| C-06 | Implement `CsvReportExporter<TRow> : ICsvReportExporter<TRow>` per D-06 — `ExportToStreamAsync` is the real encoder (`StreamWriter` over the destination `Stream`, one row at a time); `ExportAsync` composes it with `StorageStreamingWriter` (constructor-injected `IFileStorage`/optional `IBlobUriGenerator`). | SharedKernel.Reporting.Csv | `●` |
| C-07 | Implement `CsvExportOptions` (`IncludeUtf8Bom` default `true`, `Delimiter` default `,`) with `public const string SectionName` (SK0022) and `AddValidatedOptions` wiring. | SharedKernel.Reporting.Csv | `●` |
| C-08 | Implement `AddCsvReportExporter<TRow>(this IServiceCollection, ...)` DI extension registering `ICsvReportExporter<TRow>`/`CsvReportExporter<TRow>`. | SharedKernel.Reporting.Csv | `●` |
| C-09 | `[LoggerMessage]` entries at `20100`–`20199` if any are needed beyond what `StorageStreamingWriter` already logs generically — **blocked on `01.Core` shipping `Reporting = 20000`**. | SharedKernel.Reporting.Csv | `●` |
| C-10 | Implement `ISpreadsheetReportExporter<TRow> : IReportExporter<TRow>` marker interface, declared only in this package. | SharedKernel.Reporting.Spreadsheet | `●` |
| C-11 | Implement `SpreadsheetReportExporter<TRow> : ISpreadsheetReportExporter<TRow>` per D-07/D-08 — `ExportToStreamAsync` builds the `XLWorkbook`/`IXLWorksheet` row-by-row from the streamed source (no `List<TRow>` buffering) and calls the synchronous `workbook.SaveAs(stream)`; `ExportAsync` composes it with `StorageStreamingWriter` identically to C-06. XML doc on the class carries the D-08 CAPS memory-limitation statement verbatim. | SharedKernel.Reporting.Spreadsheet | `●` |
| C-12 | Implement `SpreadsheetExportOptions` (e.g. `SheetName` default, header row bold/styling default) with `SectionName`/`AddValidatedOptions`, and `AddSpreadsheetReportExporter<TRow>(...)` DI extension. | SharedKernel.Reporting.Spreadsheet | `●` |
| C-13 | `[LoggerMessage]` entries at `20200`–`20299` — **blocked on `01.Core` shipping `Reporting = 20000`**. | SharedKernel.Reporting.Spreadsheet | `●` |
| C-14 | Implement `IPdfReportExporter<TRow> : IReportExporter<TRow>` marker interface, declared only in this package. | SharedKernel.Reporting.Pdf | `●` |
| C-15 | Implement `PdfReportExporter<TRow> : IPdfReportExporter<TRow>` per D-09/D-10 — `ExportToStreamAsync` builds the MigraDoc `Document`/`Table` row-by-row from the streamed source, renders via `PdfDocumentRenderer`, saves the resulting `PdfSharp` `PdfDocument` to the destination stream; `ExportAsync` composes it with `StorageStreamingWriter`. XML doc carries the D-10 scope-boundary and memory-model statements verbatim. | SharedKernel.Reporting.Pdf | `●` |
| C-16 | Implement `PdfExportOptions` (e.g. `Title`, page size/orientation defaults) with `SectionName`/`AddValidatedOptions`, and `AddPdfReportExporter<TRow>(...)` DI extension. | SharedKernel.Reporting.Pdf | `●` |
| C-17 | `[LoggerMessage]` entries at `20300`–`20399` — **blocked on `01.Core` shipping `Reporting = 20000`**. | SharedKernel.Reporting.Pdf | `●` |

---

## Phase: Tests <!-- phase-key: SK.20.Tests -->

> The memory-boundedness proof is the load-bearing test here — but it is only a *genuine* proof for `.Csv`. `.Spreadsheet`/`.Pdf` cannot honestly claim it (D-08/D-10) — their tests instead prove single-pass streaming from the row source (no `List<TRow>` buffering step) and output correctness, which is what those two providers can actually guarantee.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | `ReportValueFormatting.Format` culture-formatting tests — a decimal under `de-DE` (comma decimal separator) and `en-US`, a `DateTime` under both, `null` → `null`, confirming this is BCL `CultureInfo` behavior with zero translation-catalog involvement (Invariant 3). | SharedKernel.Reporting.Abstractions.Tests | `●` |
| T-02 | `StorageStreamingWriter` composition test against `16.Testing`'s in-memory `IFileStorage` double — proves the encoding delegate and the upload happen concurrently (bytes reach the fake store before the encoding delegate fully completes for a large-enough row count), and that a thrown exception from the encoding side surfaces as a failed `Result`/faulted task on the upload side rather than hanging. | SharedKernel.Reporting.Abstractions.Tests | `●` |
| T-03 | `ReportExportOutcome.RowCount` correctness — matches the actual number of rows enumerated from the source, computed without a second pass. | SharedKernel.Reporting.Abstractions.Tests | `●` |
| T-04 | **The load-bearing constant-memory proof.** Stream a row count large enough (e.g. several million small rows) through `CsvReportExporter<TRow>.ExportToStreamAsync` and assert process/allocated memory stays bounded and does not grow proportionally with row count — the test that would fail immediately if a future edit accidentally introduced a `List<TRow>`/`ToListAsync()` anywhere in the CSV path. | SharedKernel.Reporting.Csv.Tests | `●` |
| T-05 | RFC 4180 escaping correctness — fields containing `,`, `"`, `\r`, `\n`, and a combination of all four in one field; embedded-quote doubling; `de-DE` decimal-comma values correctly quoted by the general rule (D-06) with no numeric special-casing anywhere in the encoder. | SharedKernel.Reporting.Csv.Tests | `●` |
| T-06 | UTF-8 BOM presence/absence per `CsvExportOptions.IncludeUtf8Bom`, both values. | SharedKernel.Reporting.Csv.Tests | `●` |
| T-07 | Single-pass-over-source proof for `SpreadsheetReportExporter<TRow>` — an `IAsyncEnumerable<TRow>` instrumented to fail on a second enumeration attempt still produces a correct workbook, proving D-07's "no `List<TRow>` buffering" claim rather than merely asserting it in a doc comment. | SharedKernel.Reporting.Spreadsheet.Tests | `●` |
| T-08 | Output correctness — a workbook exported then re-opened via `ClosedXML` itself confirms header row, ordinal-respecting column order, and formatted cell values match the source rows exactly. | SharedKernel.Reporting.Spreadsheet.Tests | `●` |
| T-09 | Single-pass-over-source proof for `PdfReportExporter<TRow>`, mirroring T-07. | SharedKernel.Reporting.Pdf.Tests | `●` |
| T-10 | **Multi-page table / page-break proof.** Export enough rows to force MigraDoc's table renderer across more than one page; assert the rendered `PdfDocument.PageCount > 1` and that no row's content is duplicated or dropped across the page boundary — the case flagged in P-480's acceptance criteria as most likely to silently break with a lower-level PDF library. | SharedKernel.Reporting.Pdf.Tests | `●` |
| T-11 | End-to-end delivery composition test (any one provider is sufficient to prove the shared path) — `ExportAsync` against `16.Testing`'s in-memory `IFileStorage`/`IBlobUriGenerator` doubles, asserting the returned `ReportExportOutcome.StoredFile` matches what was uploaded and `DownloadUrl` is populated only when `ReportDestination.PresignedDownloadUrlExpiry` was set. | SharedKernel.Reporting.Csv.Tests | `●` |

---

## Phase: Docs <!-- phase-key: SK.20.Docs -->

> XML docs, package `README.md` wired into the pack via `PackageReadmeFile`, the out-of-scope cross-references, and third-party licence attribution for ClosedXML and PdfSharp/MigraDoc.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML docs across `.Abstractions`' public surface (`IReportExporter<TRow>`, the model records, `StorageStreamingWriter`), including the D-12 scope-boundary cross-references and the Invariant-5 PII statement in capitals directly on `IReportExporter<TRow>`. | SharedKernel.Reporting.Abstractions | `●` |
| DO-02 | `SharedKernel.Reporting.Abstractions/README.md` — the two-member contract shape, the column/definition model, a DI-registration-agnostic usage sketch (concrete providers own their own DI extension), wired into the pack via `PackageReadmeFile`. | SharedKernel.Reporting.Abstractions | `●` |
| DO-03 | `SharedKernel.Reporting.Csv/README.md` — DI quick-start (`AddCsvReportExporter<TRow>()`), the RFC 4180 escaping guarantee, the BOM default and why, and an explicit statement that this is the one provider genuinely safe for unbounded row counts. | SharedKernel.Reporting.Csv | `●` |
| DO-04 | XML doc on `SpreadsheetReportExporter<TRow>` carrying the D-08 CAPS memory-limitation statement verbatim, plus MIT licence attribution comment referencing ClosedXML. | SharedKernel.Reporting.Spreadsheet | `●` |
| DO-05 | `SharedKernel.Reporting.Spreadsheet/README.md` — DI quick-start, the D-08 memory-limitation statement in capitals with a pointer to `.Csv` for large row counts, ClosedXML MIT licence attribution recorded per the root ratified-licensing table. | SharedKernel.Reporting.Spreadsheet | `●` |
| DO-06 | XML doc on `PdfReportExporter<TRow>` carrying the D-09 scope boundary and D-10 memory-model statements verbatim, plus MIT licence attribution comments for both PdfSharp and MigraDoc, and the QuestPDF/iText7-declined-on-licence rationale restated locally (not only in `CLAUDE.md`). | SharedKernel.Reporting.Pdf | `●` |
| DO-07 | `SharedKernel.Reporting.Pdf/README.md` — DI quick-start, the simple-tabular-layout-only scope statement in capitals, the D-10 memory-model caveat, PdfSharp/MigraDoc MIT licence attribution. | SharedKernel.Reporting.Pdf | `●` |
| DO-08 | A `consumer-verify`-style usage snippet in all four READMEs showing DI registration resolving cleanly through a real `IHost.StartAsync()`, foreshadowing the Published-phase harness (P-04). | All four | `●` |

---

## Phase: Published <!-- phase-key: SK.20.Published -->

> Full NuGet metadata, clean `dotnet pack`, and a `consumer-verify` harness proving each provider's DI registration resolves through a real `IHost.StartAsync()`.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Finalize NuGet metadata on all four `.csproj`s (`Description`, `PackageTags`, `PackageReadmeFile` pointing at each package's own `README.md`). | All four | `●` |
| P-02 | Clean `dotnet pack` of all four packages with no warnings — `.Spreadsheet`/`.Pdf` are blocked until the `Directory.Packages.props` pins (Cross-Domain Dependencies) land. | All four | `●` |
| P-03 | `consumer-verify` harness — a throwaway consumer project referencing all four packages, proving `AddCsvReportExporter<TRow>()`/`AddSpreadsheetReportExporter<TRow>()`/`AddPdfReportExporter<TRow>()` resolve through a real `IHost.StartAsync()` per the README recipes (DO-08), each against a real (or `16.Testing` in-memory) `IFileStorage`. | All four | `●` |
| P-04 | Verify all four packages carry zero `<Version>`/`<VersionPrefix>` elements and pack at the repo-wide MinVer-derived version (root Package Versioning rule). | All four | `●` |
| P-05 | Once every phase key in this file is `●`, run `/state-map-phase` to close P-477–P-480 in the root Phase Backlog (Step S8c — no single lifecycle key closes them individually, per the Phase Key Registry note above). | All four | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | ⚑ Blocked | State |
| --- | --- | :---: | :---: | :---: | :---: | :---: |
| `SK.20.Design` | Design | 12 | 12 | 0 | 0 | `●` |
| `SK.20.Scaffold` | Scaffold | 7 | 7 | 0 | 0 | `●` |
| `SK.20.Core` | Core | 17 | 17 | 0 | 0 | `●` |
| `SK.20.Tests` | Tests | 11 | 11 | 0 | 0 | `●` |
| `SK.20.Docs` | Docs | 8 | 8 | 0 | 0 | `●` |
| `SK.20.Published` | Published | 5 | 5 | 0 | 0 | `●` |

**Superseded status (2026-09-26): 60 of 60 tasks `●`** — S-05 and P-05 were closed afterwards (their rows are `●`); the packages ship with the repo-wide release train. Original note: **Implemented end to end 2026-09-04** (58 of 60 tasks `●`). All four packages built, tested (54 tests passing across the four `.Tests` projects — 19 Abstractions, 19 Csv, 8 Spreadsheet, 8 Pdf), documented, and packed cleanly via `dotnet pack`; a `consumer-verify` harness (`20.Reporting/consumer-verify`) proves all three providers resolve together through a real `IHost.StartAsync()` and round-trip a real export through `IFileStorage` for each format.

Two tasks remain open, both **deliberately**, per this session's explicit shared-file dispatch protocol (concurrent domain implementers were running against the same repo):
- **S-05** (register the four projects + solution folder in `Platform.SharedKernel.slnx`) — skipped on explicit instruction; the four new project paths are recorded in this file's Package Board / the domain brain for whoever owns `.slnx` next.
- **P-05** (run `/state-map-phase` to close P-477–P-480 in the root Phase Backlog) — this session was explicitly barred from touching the root `state-map.md`/`CLAUDE.md`. `SK.20.Design`, `SK.20.Core`, `SK.20.Tests`, and `SK.20.Docs` are all genuinely `●` and ready for root promotion (`Root Backlog ID` is `—` on every lifecycle row per the Phase Key Registry note, so root closure happens via Step S8c once every phase key here reads `●`/`—`) — only `SK.20.Scaffold`/`SK.20.Published` block that (both solely on S-05/P-05 above). The next session touching root state should run `/state-map-phase` once S-05 is done to close P-477–P-480.

Two Cross-Domain Dependencies blockers recorded 2026-08-26 are resolved: the `Directory.Packages.props` pins (S-03/S-04/P-02) were added this session per explicit dispatch authorization, and `01.Core` shipped `LoggingEventIdRanges.Reporting = 20000` (confirmed by reading the source directly) between that dispatch and this session, unblocking C-02/C-09/C-13/C-17.

---

## Changelog

- [2026-08-26] Domain founded — folder, state-map, and CLAUDE.md created ahead of WO-077 dispatch (P-477–P-480)
- [2026-08-26] First dispatch processed. All six phases populated end to end for all four packages (D-01–D-12, S-01–S-07, C-01–C-17, T-01–T-11, DO-01–DO-08, P-01–P-05 — 60 tasks total). Two design findings verified rather than assumed: (1) neither `ClosedXML` nor `PdfSharp`/`MigraDoc.*` is pinned in root `Directory.Packages.props` (confirmed via direct grep), recorded as a hard Scaffold/Published blocker outside this domain's jurisdiction (S-03, S-04, P-02, all `⚑`); (2) web research confirmed `ClosedXML` has no incremental/streaming write path — `XLWorkbook.SaveAs` fully materializes the workbook object graph before writing (a documented real-world case: 32 MB output, 1+ GB peak memory) — recorded as a permanent, honestly-documented limitation (D-08) rather than a defect or a false streaming claim, with the identical reasoning extended to `.Pdf`'s MigraDoc/PdfSharp pairing (D-10), mitigated there by the deliberately bounded "simple tabular/statement" scope rather than by any streaming capability. Locked the concrete mechanism behind Invariant 2 — a `System.IO.Pipelines.Pipe`-based `StorageStreamingWriter` (D-05) that lets bytes flow to `IFileStorage.UploadAsync` as they are encoded, verified against `IFileStorage`'s real signature (`FileUploadRequest.Content` is a plain caller-owned `Stream` read from its current position, no upfront length needed) rather than assumed. Locked provider-exclusive marker interfaces (`ICsvReportExporter<TRow>`/`ISpreadsheetReportExporter<TRow>`/`IPdfReportExporter<TRow>`) as the DI-ergonomics answer to "three providers implement one generic contract," mirroring `09.Search`/`10.Intelligence`'s existing compile-time provider-exclusivity precedent. Updated the `01.Core` `LoggingEventIdRanges` Cross-Domain Dependencies row from "absent" to "design-locked, implementation pending" per this session's `SK.01.LoggingRangesNewDomains` phase key, confirmed by reading the source file directly (no `Reporting` entry in code yet, consistent with `18.Idempotency`/`19.Scheduling`'s identical status).
- [2026-09-04] Domain implemented end to end (58/60 tasks `●`; SK.20.Design/Core/Tests/Docs all `●`, SK.20.Scaffold/Published `◐` — see Overall Progress for the two deliberately-open tasks, S-05 and P-05). Both prior blockers resolved: pinned `ClosedXML 0.105.1` + `PDFsharp 6.2.4` + `PDFsharp-MigraDoc 6.2.4` in root `Directory.Packages.props` (a corrected two-package-ID mapping for the PdfSharp/MigraDoc pairing — the originally-planned three-way split no longer exists on nuget.org, verified directly); confirmed `01.Core` shipped `LoggingEventIdRanges.Reporting = 20000` in code, unblocking every `[LoggerMessage]` entry. Built `StorageStreamingWriter`, all three providers, and their DI extensions exactly to the locked Design spec. One real implementation-time discovery not anticipated in Design: `PdfSharp.Pdf.PdfDocument.Save` requires a stream supporting a readable `Position` (to compute xref byte offsets while writing), which the `Pipe`-backed stream `StorageStreamingWriter` hands every provider does not support — worked around by having `PdfReportExporter` render into a local, fully-seekable `MemoryStream` buffer first and copying it to the real destination afterward, which costs nothing beyond what D-10's memory-model concession already accepts. Also had to embed a font (Roboto, Apache License 2.0) via a custom `IFontResolver`, since PdfSharp 6.x performs no implicit OS font enumeration on any platform — not anticipated by D-09/D-10, recorded in `SharedKernel.Reporting.Pdf/README.md` and the domain brain. 54/54 tests passing across four `.Tests` projects; `dotnet pack` clean on all four; a new `20.Reporting/consumer-verify` harness proves all three providers compose in one host and each round-trips a real export through `IFileStorage`. Root `state-map.md`/`CLAUDE.md` deliberately left untouched this session (concurrent-domain-implementer shared-file protocol) — flagged for the next session to run `/state-map-phase` and promote Design/Core/Tests/Docs (and Scaffold/Published once S-05/P-05 close).
- [2026-09-04] Coordinator follow-up — rows left open by this domain's own implementation session because the blocker sat outside its lane are now closed: `.slnx` registration was performed centrally (149 projects, full-solution build 0 errors), the root Phase Backlog promotion ran, and the Docker daemon became available so every Testcontainers-backed proof executed for real. Verified this session: `SharedKernel.Idempotency.Redis.Tests` 25/25, `SharedKernel.Idempotency.EfCore.Tests` 25/25, `SharedKernel.Scheduling.Tests` 35/35 (including `MultiReplicaSingleExecutionTests`), `SharedKernel.Reporting.*.Tests` all green, full solution 6,945 passed / 0 failed / 2 skipped across 66 assemblies (coordinator)
- [2026-09-26] **WO-086 foundation refactor recorded (P-565, P-571, P-574, P-575).** P-565: `ReportDestination.TenantId` is `TenantId?` (`SharedKernel.Execution.Tenancy`; `default(TenantId)` rejected, `null` for a shared store), matching `08.Storage`'s tenant views. P-571: `InMemoryReportExporter<TRow>` moved from `SharedKernel.Testing` to `16.Testing/SharedKernel.Reporting.Testing`. P-574: tiers — `.Abstractions` = Abstractions, `.Csv`/`.Spreadsheet`/`.Pdf` = Adapter — enforced by the build. P-575: `CLAUDE.md` "Layering" replaced by "Tiers and references", Open Items cleared (S-05/P-05 were already `●`), changelog moved below; the Overall Progress table corrected to 60/60; package READMEs gained install/tier lines. — WO-086, P-575
- [2026-09-26] **Pre-publish gold-standard pass (direct user request).** Why: an audit before first publish found CSV formula injection (CWE-1236), PDF tables overflowing the page (10 columns rendered 25 cm wide on a 21 cm A4), every Excel cell written as text, ClosedXML building whole workbooks in memory, an upload failure not stopping the row source, fabricated exceptions in failure logs, no error status on spans, one registration per row type, and no PublicAPI tracking. User decisions: full breaking cleanup (nothing published yet); streaming spreadsheet; trusted community packages over hand-written ones; all four feature groups (runtime format choice, richer delivery, telemetry, PDF polish); a new HTML-to-PDF converter; a DocumentsApi sample. Result: one namespace and chain (`AddSharedKernelReporting().AddCsv().AddSpreadsheet().AddPdf().AddGotenberg()`), open-generic exporters, `IReportExporterFactory` + keyed services, fluent `ReportDefinition.For<T>()` (`Ordinal` removed), `ReportExporterBase<T>`/`HtmlToPdfConverterBase`, internal pipe delivery that never stores a half-written report and stops the writer when the upload stops, `DownloadFileName`/`Condition`/`SizeBytes`/`Format`, a `SharedKernel.Reporting` meter + `WithReportingTelemetry()`, `ReportingErrorCodes`, PublicAPI on all packages. Spreadsheet moved to SpreadCheetah (streaming, typed cells); PDF widths, page numbers and options fixed; new `SharedKernel.Reporting.Gotenberg`. Tests: 60 + 22 + 25 + 8 + 18 (real Gotenberg) + 6 fakes; consumer-verify rewritten; DocumentsApi 66/66 from packed packages.

---

## Domain-Brain Changelog

> Moved here from `CLAUDE.md` by P-575 (WO-086). History only; `CLAUDE.md` describes the current state.

- [2026-09-04] All four packages implemented, tested (54 tests), documented, and packed end to end (WO-077, phase-implementer)
- [2026-09-22] Delivery docs updated for `08.Storage`'s P-559 redesign: `ReportDestination` names a store (and tenant), resolved through `IFileStorageFactory`; the presigned download comes from the store itself as a `PresignedRequest` (coordinator)
- [2026-09-26] Brain rewritten for the pre-publish pass: SpreadCheetah replaces ClosedXML (licensing table records why), new `.Gotenberg` package and HTML-to-PDF decision, invariants restated per provider (constant memory for CSV/Excel, `MaxRows` for PDF), Design and What-Goes-Where tables for the new API (coordinator)
