# 20.Reporting — Domain Brain

## What This Domain Is

Streaming, memory-bounded generation of **structured tabular output** — statements, regulatory reports, bulk data exports — in CSV, spreadsheet, and PDF form, delivered through object storage rather than buffered back through an HTTP response.

The word that matters in that sentence is **streaming**. The failure mode this domain exists to prevent is a service materializing an entire result set into memory to build a report, which works fine in staging and falls over on the tenant with two million rows.

---

## Packages

```
SharedKernel.Reporting.Abstractions   → IReportExporter<TRow>, column/field model, delivery composition
SharedKernel.Reporting.Csv            → RFC 4180, hand-written, zero third-party NuGet
SharedKernel.Reporting.Spreadsheet    → ClosedXML
SharedKernel.Reporting.Pdf            → PdfSharp / MigraDoc
```

Standard `.Abstractions` + `.{Provider}` split — three providers exist, so the split is mandatory, not optional. Provider packages are **siblings**: none references another, and there is no shared `.Core` between them.

---

## Third-Party Licensing — a ratified decision, not a default

This was settled during WO-077 planning and must not be quietly revisited by a future session reaching for a more familiar library.

| Library | Verdict | Reason |
|---|---|---|
| **ClosedXML** | ✅ Adopted (spreadsheet) | Unconditional MIT |
| **PdfSharp / MigraDoc** | ✅ Adopted (PDF) | Unconditional MIT |
| EPPlus | ❌ Declined | PolyForm Noncommercial — unusable in a commercial platform |
| QuestPDF | ❌ Declined | Revenue-gated commercial licence above a threshold |
| iText7 | ❌ Declined | AGPL — copyleft obligations unacceptable for a distributed NuGet package |

The two adopted libraries are less ergonomic than the two most popular declined ones. That is the cost of the licence constraint and it was accepted knowingly. If a format genuinely cannot be served by an acceptably-licensed dependency, **scope that format out** rather than shipping a phase that cannot be completed — do not introduce a copyleft or revenue-gated dependency into a package the platform publishes.

**Shipped 2026-09-04.** `Directory.Packages.props` now pins `ClosedXML` `0.105.1` and, for PDF, `PDFsharp` `6.2.4` + `PDFsharp-MigraDoc` `6.2.4` — both MIT, verified directly against each published nuspec before pinning. **Correction to the original plan, verified against the live NuGet index, not assumed:** the three-way `PdfSharp` + `MigraDoc.DocumentObjectModel` + `MigraDoc.Rendering` package split this section originally named no longer exists on nuget.org. The current, actively-maintained PDFsharp-team packages are exactly two IDs — `PDFsharp` (core PDF primitives) and `PDFsharp-MigraDoc` (bundles the `MigraDoc.DocumentObjectModel`/`MigraDoc.Rendering` namespaces, depends on `PDFsharp` at the identical pinned version) — a corrected package-ID mapping onto the same licence-ratified technology, never a substitution. Do not "fix" the `<PackageVersion>` entries in `Directory.Packages.props` back to the old three-ID split; it will fail restore.

**ClosedXML's real memory behavior — verified by research, not assumed, during WO-077 Design (D-08).** ClosedXML exposes no incremental/streaming write path. `XLWorkbook.SaveAs` builds the entire workbook object graph in memory and only serializes it to the destination stream when called — a documented real case saw a 32 MB `.xlsx` output cost 1+ GB of peak process memory (`ClosedXML/ClosedXML#1180`). `SharedKernel.Reporting.Spreadsheet` therefore **cannot honestly claim to be memory-bounded**, and must not imply otherwise anywhere in its docs. What it *can* and does guarantee: it never additionally buffers the row source into a `List<TRow>` before feeding ClosedXML (avoiding paying that cost twice), and its own XML docs/README state the ClosedXML limitation in capitals with a pointer to `.Csv` for genuinely large exports. This is recorded as a **permanent, accepted characteristic of the chosen dependency**, not a defect for a future phase to "fix" — no evaluated MIT-licensed alternative offers true `.xlsx` streaming at an acceptable ergonomic/implementation-risk cost (the SAX-style `DocumentFormat.OpenXml` primitive does stream, but was already weighed against ClosedXML and declined for that reason when P-479 was scoped). The identical reasoning and the identical documentation obligation apply to `.Pdf`'s MigraDoc/PdfSharp pairing, mitigated there by that provider's deliberately narrow "simple tabular/statement layout" scope rather than by any streaming capability MigraDoc doesn't have either.

---

## Layering

```
20.Reporting → may reference 01.Core, 08.Storage.Abstractions
```

That is the complete list, and it is deliberately austere.

**This domain never references `06.Persistence`.** The caller supplies the `IAsyncEnumerable<TRow>`. Composing with `06.Persistence`'s already-shipped `IAsyncEnumerable` streaming reads (`StreamAsync`) and keyset cursor pagination (`ListKeysetAsync`) happens **in consumer code**, not through a reference here. A reference would let this domain grow its own data-access opinions, which is exactly the duplication the streaming contract is designed to avoid.

**No inbound grant exists or is needed.** Unlike `06.Persistence`/`08.Storage`/`09.Search`/`10.Intelligence`/`17.Workflows`/`19.Scheduling`, this domain holds no persistent connection, so there is nothing to probe for readiness. It is a stateless library in the same class as `01.Core.Compression`/`.Cryptography`. **The absence of a `13.ServiceDefaults` readiness phase here is deliberate, not an omission** — this is stated in P-477's acceptance criteria precisely so a future session does not "notice the gap" and add one.

**Composition with higher-numbered domains needs no grant in either direction.** A long-running export driven by `19.Scheduling` or `17.Workflows` composes inside the consuming service's own job body or activity, which may reference any package regardless of number. The platform's downward-only numbering constrains *SharedKernel packages referencing each other*, not consumer code referencing several SharedKernel packages.

---

## Domain Invariants

**1 — The primary contract is streaming, with no escape hatch.** `IReportExporter<TRow>` accepts `IAsyncEnumerable<TRow>` on both of its members — `ExportAsync` (storage-delivered) and `ExportToStreamAsync` (the Invariant-2 convenience path). **No overload accepting `IEnumerable<TRow>` or `List<TRow>` may exist anywhere on the contract** — the moment one does, every caller uses it and the memory-boundedness guarantee is gone. This is the domain's central invariant.

**Important scope note, added after ClosedXML's actual behavior was verified (not assumed) during WO-077 Design:** this invariant governs the *contract shape* — no materializing overload, ever. It is **not** a claim that every provider's underlying encoding is itself O(1)-memory. `.Csv` genuinely is. `.Spreadsheet` (ClosedXML) and `.Pdf` (MigraDoc/PdfSharp) are **not** — both third-party libraries build their full document object model in memory before writing a byte to the destination stream, a verified, permanent characteristic of those dependencies, not a defect awaiting a fix. Never let this invariant's wording be read as "every provider streams to disk row by row" — say plainly, per provider, what is and is not true, in capitals in that provider's own XML docs. See the Third-Party Licensing / Providers section below for the specifics.

**2 — Delivery goes through storage, never through the response.** Output is written via `08.Storage.Abstractions`' `IFileStorage`, with a presigned URL returned via `IBlobUriGenerator`. No code path may offer a fully-buffered byte array as the *sole* option. A small-output convenience path may exist (`ExportToStreamAsync`), but never as the only way out. The concrete mechanism making this real, not aspirational: `StorageStreamingWriter` (`.Abstractions`) opens a `System.IO.Pipelines.Pipe` and runs `IFileStorage.UploadAsync` concurrently against the pipe's reader-side `Stream` while a provider's encoder writes into the writer side — bytes reach storage as they are produced, with no intermediate byte-array buffering at the delivery layer, regardless of what a given provider's own encoding step does internally (see Invariant 1's note above — the pipe removes delivery-layer buffering; it cannot remove a third-party library's own in-memory document model).

**3 — Formatting is `CultureInfo`, not translation.** Per-column formatters accept a `CultureInfo`. Formatting numbers, dates, and currency by culture is a **BCL capability**. This domain takes **no dependency on `SharedKernel.Localization`** (WO-078) or any translation catalog — a column *header* that needs translating is resolved by the caller before it reaches the column definition. Blurring these two concerns would drag a translation catalog into every export.

**4 — Scope boundaries are documented in XML, not just here.** Tenant provisioning, data classification/redaction, and scheduling are all explicitly out of scope for this domain, and the XML docs must cross-reference where each actually lives (`13.ServiceDefaults` / `01.Core.DataPrivacy` / `19.Scheduling`). Exports are a natural place for each of those concerns to accidentally accrete.

**5 — PII is the caller's problem, and must be said out loud.** An export is a bulk extraction of data to a durable file with a shareable URL — the highest-consequence PII surface the platform has. This domain performs **no** classification or redaction of its own (that is `01.Core/SharedKernel.DataPrivacy`, WO-076). The docs must state plainly that rows arrive already-redacted or they leave un-redacted; there is no safety net here.

**6 — Providers are independently swappable.** No shared `.Core`, no cross-provider references, no base class holding "common" encoding logic. Duplication between providers is accepted, as it is in `08.Storage`'s `.S3`/`.Obs` pair.

---

## Technology

| Concern | Choice | Notes |
|---|---|---|
| Row source | Caller-supplied `IAsyncEnumerable<TRow>` | This domain never opens a connection or issues a query |
| Contract shape | `IReportExporter<TRow>.ExportAsync` (storage-delivered) + `.ExportToStreamAsync` (Invariant-2 convenience path) | Both members take `IAsyncEnumerable<TRow>` — never `IEnumerable`/`List<TRow>` on either |
| Provider discrimination | Marker interfaces (`ICsvReportExporter<TRow>`, `ISpreadsheetReportExporter<TRow>`, `IPdfReportExporter<TRow>`), each declared only in its own provider package | Compile-time provider exclusivity, mirroring `09.Search`/`10.Intelligence`'s established pattern — never a runtime format-string switch |
| Delivery mechanism | `StorageStreamingWriter` (`.Abstractions`) — `System.IO.Pipelines.Pipe` running `IFileStorage.UploadAsync` concurrently against a provider's own `ExportToStreamAsync` encoder | BCL only, no NuGet reference; makes Invariant 2 concrete rather than aspirational |
| CSV encoding | Hand-written, RFC 4180 | Zero third-party NuGet — the dependency-free baseline case; the only provider that is genuinely constant-memory end to end |
| Spreadsheet | ClosedXML `0.105.1` | Unconditional MIT; **verified to have no incremental write path — not memory-bounded, documented in capitals**; pinned in root `Directory.Packages.props` (WO-077, shipped 2026-09-04) |
| PDF | `PDFsharp` `6.2.4` + `PDFsharp-MigraDoc` `6.2.4` (two NuGet IDs — see Third-Party Licensing above for why this replaced the originally-planned three-ID split) | Unconditional MIT; same in-memory-document-model constraint as ClosedXML, mitigated by the deliberately narrow tabular/statement scope; pinned in root `Directory.Packages.props` (WO-077, shipped 2026-09-04). **`PdfDocument.Save` needs a `Position`-readable stream** (to compute xref byte offsets while writing) — the `Pipe`-backed stream `StorageStreamingWriter` hands every provider does not support this, so `PdfReportExporter` renders into a local `MemoryStream` buffer first and copies it to the real destination afterward; costs nothing beyond what the memory-model concession above already accepts, but is a real implementation constraint future PDF work must not "optimize away" by trying to `Save` straight to the pipe stream. **PdfSharp 6.x performs no implicit OS font enumeration on any platform** — `SharedKernel.Reporting.Pdf` embeds the Roboto font family (Apache License 2.0, `Fonts/Roboto-{Regular,Bold}.ttf` + `Fonts/LICENSE.txt`) via a custom `IFontResolver` (`EmbeddedRobotoFontResolver`/`PdfFontResolverRegistration`) rather than reading a host-installed font — required for identical behavior between local Windows development and the Linux containers this platform deploys to; there is no font-family configuration option, by design |
| Delivery | `08.Storage.Abstractions` | `IFileStorage.UploadAsync` (verified: reads a plain caller-owned `Stream` from its current position, no upfront length required — exactly what the `Pipe`-based writer needs), `IBlobUriGenerator` presigned read |
| Formatting | BCL `CultureInfo`, via a shared `ReportValueFormatting` default helper in `.Abstractions` | Never a translation catalog |
| Options validation | `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions` | Provider packages only |
| Logging | `[LoggerMessage]`, EventIds `20000`–`20999`, sub-blocks `.Abstractions` 20000-20099 / `.Csv` 20100-20199 / `.Spreadsheet` 20200-20299 / `.Pdf` 20300-20399 | `Reporting = 20000` **shipped in `01.Core`'s `LoggingEventIdRanges`** — confirmed by reading the source file directly 2026-09-04. `StorageStreamingWriter`'s three generic entries (export-started/completed/failed, `.Abstractions` sub-block) cover every provider; no provider-specific event was found to need its own entry in `.Csv`/`.Spreadsheet`/`.Pdf`'s own 100-wide sub-blocks — a deliberate decision, not an oversight |

---

## What Goes Where (within this domain)

| I need to add… | It belongs in… |
|---|---|
| A change to the export contract or column model | `SharedKernel.Reporting.Abstractions` — `IReportExporter<TRow>`, `ReportColumn<TRow>`/`ReportDefinition<TRow>`/`ReportDestination`/`ReportExportOutcome` |
| A change to how bytes reach storage (retries, buffer sizing, upload concurrency) | `SharedKernel.Reporting.Abstractions` — `StorageStreamingWriter`; never re-implemented per provider |
| A format-specific encoding behavior | The owning provider package — never the abstraction. Implement it inside that provider's `ExportToStreamAsync`; `ExportAsync` composes it with `StorageStreamingWriter` automatically |
| A fourth output format | A new sibling `SharedKernel.Reporting.{Format}`, licence-checked first, with its own marker interface (`I{Format}ReportExporter<TRow>`) mirroring `ICsvReportExporter<TRow>`/`ISpreadsheetReportExporter<TRow>`/`IPdfReportExporter<TRow>` |
| A convenience overload accepting `IEnumerable<TRow>`/`List<TRow>` | **Declined, structurally.** The moment one exists on `IReportExporter<TRow>`, every caller uses it and Invariant 1 is gone. If a caller genuinely has an in-memory collection, `MyList.ToAsyncEnumerable()` (a one-line BCL/`System.Linq.Async` adapter) is the caller's problem to solve, not this domain's contract to weaken |
| Anything that queries a database | **Not here.** The caller streams rows in |
| Redaction or PII masking | **Not here.** `01.Core/SharedKernel.DataPrivacy` (WO-076), applied before rows reach the exporter |
| Translated column headers | **Not here.** Resolved by the caller; this domain only formats by `CultureInfo` |
| Running an export on a schedule | **Not here.** `19.Scheduling` fires it; the composition lives in consumer code |
| An `IHealthCheck` or readiness probe | **Nowhere.** This domain is stateless by design — see Layering |
| An in-memory test double | `16.Testing/SharedKernel.Testing` (P-481) |

---

## Open Items

- **All four packages are implemented, tested, documented, and packed — shipped 2026-09-04.** `Directory.Packages.props` pins `ClosedXML`/`PDFsharp`/`PDFsharp-MigraDoc` (see Third-Party Licensing) and `01.Core`'s `LoggingEventIdRanges.Reporting = 20000` is live in code — both prior blockers on this list are resolved and no longer apply. See `state-map.md`'s Overall Progress for the full 58/60-task breakdown.
- Two `state-map.md` tasks remain open **deliberately**, both process/administrative rather than technical: **S-05** (register the four projects + a `20.Reporting` solution folder in `Platform.SharedKernel.slnx` — the four project paths are recorded in `state-map.md`'s Package Board) and **P-05** (run `/state-map-phase` to promote `SK.20.Design`/`.Core`/`.Tests`/`.Docs` — all genuinely `●` — to the root `state-map.md`, and close root Phase Backlog P-477–P-480). Both were skipped this session under an explicit shared-file dispatch protocol barring edits to `Platform.SharedKernel.slnx` and the root `state-map.md`/`CLAUDE.md` (concurrent domain implementers were running against the same repo). Whoever next has clearance to touch those root files should perform both.
- `16.Testing`'s in-memory `IReportExporter<TRow>` fake (P-481) remains that domain's own, separately-dispatched work — this domain's implementation does not block it; the exact `IReportExporter<TRow>` two-member contract shape is now real, compiled code (`SharedKernel.Reporting.Abstractions.Exporters.IReportExporter<TRow>`) for P-481 to build against.

---

## Changelog

- [2026-09-04] All four packages implemented, tested (54 tests), documented, and packed end to end (WO-077, phase-implementer)
