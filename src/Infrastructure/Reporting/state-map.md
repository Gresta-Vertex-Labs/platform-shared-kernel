# 20.Reporting — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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

Test doubles: `InMemoryReportExporter`/`InMemoryReportExporterFactory`/`InMemoryHtmlToPdfConverter` + `AddInMemoryReporting()` in `src/Testing/SharedKernel.Reporting.Testing`. EPPlus, QuestPDF and iText7 declined on licensing.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.20.Design` | Design (D-01–D-12) | ● |
| `SK.20.Scaffold` | Scaffold (S-01–S-07) | ● |
| `SK.20.Core` | Core (C-01–C-17) | ● |
| `SK.20.Tests` | Tests (T-01–T-11) | ● |
| `SK.20.Docs` | Docs (DO-01–DO-08) | ● |
| `SK.20.Published` | Published (P-01–P-05) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. The domain never references persistence (the caller supplies the `IAsyncEnumerable<TRow>`); delivery goes through `08.Storage`'s `IFileStorageFactory`.

## Completed Phases

- Pre-publish gold-standard pass ● CSV injection guard, PDF column fit, typed Excel cells, SpreadCheetah replaces ClosedXML, new `.Gotenberg` package, factory and fluent definition API (2026-09-26)
- WO-086 ● `ReportDestination.TenantId` is `TenantId?`; fakes moved to `SharedKernel.Reporting.Testing`; tiers declared (P-565, P-571, P-574, P-575) (2026-09-26)
- Delivery update ● `ReportDestination` names a store, resolved through `IFileStorageFactory` (after `08.Storage` P-559) (2026-09-22)
- SK.20.Published ● Pack, metadata, `consumer-verify`; root P-477–P-480 closed (2026-09-04)
- SK.20.Docs ● Four READMEs and CLAUDE.md (2026-09-04)
- SK.20.Tests ● 54 tests across four projects (2026-09-04)
- SK.20.Core ● Exporters, delivery pipe, telemetry (2026-09-04)
- SK.20.Scaffold ● Projects, package pins, `.slnx` registration (2026-09-04)
- SK.20.Design ● D-01–D-12 (WO-077) (2026-08-26)

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] Pre-publish gold-standard pass (direct user request): SpreadCheetah, Gotenberg, CSV injection guard, PDF overflow fix.
- [2026-09-26] WO-086 foundation refactor recorded (P-565, P-571, P-574, P-575).
- [2026-09-22] Delivery docs updated for `08.Storage`'s P-559 redesign.
- [2026-09-04] Coordinator follow-up: `.slnx` registration and root promotion done; every phase ●.
