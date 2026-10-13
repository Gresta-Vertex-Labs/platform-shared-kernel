# 09.Search — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Search.Abstractions` | Abstractions | ● | Zero third-party: `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, `IQueryBuilder`, closed `SearchFilter` AST, `SearchIndexDefinition`; `TenantScope` (from Execution) is a mandatory parameter on every read and filtered write. Open: `SearchStreamException` derives `SharedKernelException` and the package references `SharedKernel.Core` (`SK.09.SearchErrorSafety`); public `SearchErrorCodes`, `SearchTagKeys`, `SearchOperations`, renamed codes (`SK.09.ObservabilityConformance`). |
| `SharedKernel.Search.Meilisearch` | Adapter | ● | Engine-only contracts (`IInstantSearch<T>`, `ITenantSearchTokenIssuer`, ranking rules); one `search-meilisearch-{index}` probe per index. Open: no provider text in any `Error`, classify-or-rethrow, exceptions passed to logs (`SK.09.SearchErrorSafety`); error codes, telemetry, log layout conformance (`SK.09.ObservabilityConformance`). |
| `SharedKernel.Search.ElasticSearch` | Adapter | ● | Engine-only contracts (`IAnalyticsSearch<T>`, `ICursorSearch<T>`, `ISuggestSearch<T>`); one `search-elasticsearch-{index}` probe per index. Open: no `DebugInformation` in any `Error`, cursor expiry only on the real signal, classify-or-rethrow, exceptions passed to logs (`SK.09.SearchErrorSafety`); error codes, telemetry, log layout conformance (`SK.09.ObservabilityConformance`). |
| `SharedKernel.Search.Testing` | Testing | ● | `InMemorySearchIndex<T>`, provisioner and descriptor doubles. No contract changes in either open phase, so no behaviour change; it gains only the `SharedKernelException` assertion through `SearchStreamException` and follows the E3 text sweep. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Inbound (all four WO-087 Phase 0 phases are `○` until shipped).** `SK.01.ObservabilityHelpers` (P-580) is needed by both phases (`IHasError`, the failure recorder, `TelemetryVersion`, outcome and tag constants, `LoggingEventIdRanges` helpers); `SK.16.TelemetryCapture` (P-581) by the tests of both; `SK.13.WildcardTelemetry` (P-582) and `SK.00.ObservabilityReportOnly` (P-583) by `SK.09.ObservabilityConformance`. If an implementer finds one absent on disk, mark only the dependent tasks `⚑` with that evidence.
- **14.Presentation (note, not planned here).** `SearchStreamException` becomes a `SharedKernelException`, so the HTTP, gRPC and SignalR edge maps will translate it by its `ErrorType` instead of answering 500. The status assertions (Unavailable → 503 / `Unavailable`, Timeout → 504 / `DeadlineExceeded`, Unauthorized → 401, Unexpected → 500 / `Internal`) belong in `14.Presentation` tests; `SK.09.SearchErrorSafety` pins only the exception type and `ErrorType` (T-06).
- **00.Governance.** `SK0043` (P-583) lists `SearchStreamException` as a known in-repo hit; it clears when `SK.09.SearchErrorSafety` ships. `SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies` is expected to keep passing with the new `SharedKernel.Core` reference (D-01 verifies; if its forbidden-term list ever needs an exemption, that is a `00.Governance` change). Phase 3 (P-618) turns the report-only rules into errors; this domain must already be clean.
- **13.ServiceDefaults.** `SK.13.WildcardTelemetry` deletes `WithSearchTelemetry()`; the domain `CLAUDE.md` coupling row changes when this phase ships. The renamed instruments (`sharedkernel.search.operation.duration`, `sharedkernel.search.documents`) must fall under that phase's seconds-histogram view.
- **arch-lead (root `CLAUDE.md`).** Rule C2 says the capability error factory is `internal`. Search's vocabulary is built by three assemblies in separate packages (two providers and the double), so `SearchErrors` stays public by design (`SK.09.ObservabilityConformance` D-01); the same shape applies to `08.Storage` and `10.Intelligence`. Proposed C2 amendment: "internal unless a sibling assembly of the same capability must construct it".
- **16.Testing.** No double rule change; `SharedKernel.Search.Testing` follows the renames and the text sweep inside this domain's phases.
