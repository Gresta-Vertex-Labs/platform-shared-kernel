# 20.Reporting — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Reporting.Abstractions` | Abstractions | ● | P-477. Namespace `SharedKernel.Reporting`: `IReportExporter<TRow>`, `IReportExporterFactory`, `IHtmlToPdfConverter`, `ReportFormat`, `ReportDefinition.For<T>()`, `ReportExporterBase<T>`/`HtmlToPdfConverterBase`, `ReportDestination` (named `08.Storage` store, `TenantId?`), `AddSharedKernelReporting()`/`AddExporter`, `ReportingErrorCodes`. |
| `SharedKernel.Reporting.Csv` | Adapter | ● | P-478. Dependency-free RFC 4180, CSV formula-injection guard, constant memory. |
| `SharedKernel.Reporting.Spreadsheet` | Adapter | ● | P-479. SpreadCheetah (MIT) — streaming, typed cells, constant memory, `MaxRows`. ClosedXML is test-only. |
| `SharedKernel.Reporting.Pdf` | Adapter | ● | P-480. PDFsharp/MigraDoc (MIT) — simple tabular layout, page numbers, `MaxRows`. |
| `SharedKernel.Reporting.Gotenberg` | Adapter | ● | HTML → PDF over Gotenberg 8 (Chromium in its own container); resilience handler, basic auth, `gotenberg` probe; Integration-lane tests. |

Test doubles: `InMemoryReportExporter`/`InMemoryReportExporterFactory`/`InMemoryHtmlToPdfConverter` + `AddInMemoryReporting()` in `src/Infrastructure/Reporting/SharedKernel.Reporting.Testing`. EPPlus, QuestPDF and iText7 declined on licensing.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

The domain never references persistence (the caller supplies the `IAsyncEnumerable<TRow>`); delivery goes through `08.Storage`'s `IFileStorageFactory`.

- **08.Storage** — an `IFileStorage.UploadAsync` that reads a faulted pipe sees `ReportAbortedException` (a `SharedKernelException`); a provider that wraps it should keep it as the inner exception so its span can carry the cause `error.code`. No change is required of Storage.
- **15.Integration** — same propagation shape (`RequestContextPropagation.WriteHeaders` with a null context and explicit correlation id); the planners keep the two in line.
