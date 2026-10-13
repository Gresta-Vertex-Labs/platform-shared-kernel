# 10.Intelligence — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.AI.Abstractions` | Abstractions | ● | Zero third-party. Embedding generation (validates model identity, dimension, metric before I/O), vector collections with a mandatory `TenantScope` (from `SharedKernel.Execution`), `ISemanticKernel`-shaped orchestration (token usage is a result, no silent completion retry, prompt text never logged). Open: references `SharedKernel.Core` so `IntelligenceStreamException` is a `SharedKernelException`; error ErrorTypes and text contract; additive `ai.operation` and token-type constants (`SK.10.IntelligenceObservability`); public `IntelligenceErrorCodes`, `IntelligenceTagKeys`, `IntelligenceOperations`, renamed codes (`SK.10.IntelligenceConformance`). |
| `SharedKernel.AI.Qdrant` | Adapter | ● | The only vector provider (`Qdrant.Client`). Qdrant-exclusive contracts (quantization profile, hybrid query) stay in this package; one `vector-store-{provider}-{collection}` probe per collection. Verified by `consumer-verify.Qdrant` and a real-Testcontainers conformance suite. Open: spans and metrics for the data plane, no engine text or endpoint in any `Error`, classify-or-rethrow with caller cancellation always propagating, exceptions passed to logs (`SK.10.IntelligenceObservability`); control-plane spans, log layout, code conformance (`SK.10.IntelligenceConformance`). |
| `SharedKernel.AI.SemanticKernel` | Adapter | ● | LLM orchestration and embeddings over Microsoft.SemanticKernel; `IKernelPluginAccessor`. No LLM readiness probe by design (a probe would be a billed completion call). Verified by `consumer-verify.SemanticKernel`. Open: spans, duration and token metrics, no exception text in any `Error`, classify-or-rethrow, timeout and transport faults classified, SDK-level hidden retry verified (`SK.10.IntelligenceObservability`); log layout and code conformance (`SK.10.IntelligenceConformance`). |
| `SharedKernel.AI.Testing` | Testing | ● | In-memory doubles of every neutral contract. No behaviour change in either open phase; it follows the `IntelligenceErrors` signature changes and renames and the E3 text sweep. Doubles emit no telemetry. |
| `SharedKernel.AI.Milvus` | — | ⊘ | Retracted by WO-048 (2026-07-27): `Milvus.Client` never shipped a stable release. Never built. |
| `SharedKernel.AI.VectorDb` | — | ⊘ | Retired at Scaffold S-01 (replaced by the Abstractions + provider split). |

Test doubles: in-memory embedding/vector/kernel doubles in `src/Infrastructure/AI/SharedKernel.AI.Testing`; `QdrantContainerFixture` in `SharedKernel.Testing.Internal`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Inbound (all four WO-087 Phase 0 phases are `○` until shipped).** `SK.01.ObservabilityHelpers` (P-580) is needed by both phases (`IHasError`, the failure recorder, `TelemetryVersion`, outcome and tag constants, `LoggingEventIdRanges` helpers); `SK.16.TelemetryCapture` (P-581) by the tests of both; `SK.13.WildcardTelemetry` (P-582) by the reach of the new telemetry and by the seconds view; `SK.00.ObservabilityReportOnly` (P-583) by `SK.10.IntelligenceConformance`. If an implementer finds one absent on disk, mark only the dependent tasks `⚑` with that evidence.
- **13.ServiceDefaults.** `SK.13.WildcardTelemetry` deletes `WithIntelligenceTelemetry()` and subscribes `SharedKernel.*`; `SharedKernel.AI` matches it, so nothing is wired from here. The domain `CLAUDE.md` coupling row changes when that phase ships. `sharedkernel.ai.operation.duration` (histogram, unit `s`) must fall under that phase's seconds-histogram view. This domain cannot prove the end-to-end export (no reference to the Host tier); its tests assert at the recorders, `SK.13` T-01/T-02 prove the wildcard.
- **14.Presentation (note, not planned here).** `IntelligenceStreamException` becomes a `SharedKernelException`, and four `ErrorType`s change (`Unavailable` → HTTP 503 / gRPC `Unavailable`, `Timeout` → 504 / `DeadlineExceeded`, `Unexpected` → 500 / `Internal`; `engine_unauthorized` becomes `Unexpected` in Phase 2). The HTTP/gRPC/SignalR status assertions for these belong in `14.Presentation` tests; this domain pins only the exception type and `ErrorType`.
- **01.Core (note).** `ErrorType` has no rate-limit member; `rate_limited` is `Unavailable` as the `ErrorType` contract directs for a throttling dependency. If Core later adds a distinct member, revisit this one code.
- **00.Governance.** `SK0043` (P-583) lists `IntelligenceStreamException` as a known in-repo hit; it clears when `SK.10.IntelligenceObservability` ships. `IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies` is expected to keep passing with the new `SharedKernel.Core` reference (D-01 verifies). The `SK0027` XML doc still names a removed provisioner `ProbeAsync` (unchanged, `00.Governance` owns it). Phase 3 (P-618) turns the report-only rules into errors; this domain must already be clean.
- **16.Testing.** No double rule change; `SharedKernel.AI.Testing` follows the signature changes, the renames and the text sweep inside this domain's phases.
- **arch-lead (root `CLAUDE.md`).** (1) Rule C2 says the capability error factory is `internal`; `IntelligenceErrors` stays public by design (see Phase 2 D-01), the same shape as `09.Search` and `08.Storage`: proposed amendment "internal unless a sibling assembly of the same capability must construct it". (2) Root T4 puts package tag keys in `{Capability}TagKeys`; the P-608 text says "the well-known class". This plan follows T4 (`IntelligenceTagKeys`). (3) Phase 1 introduces new instruments with final names, so Phase 2 renames none; the split of P-591/P-608 is by mechanism (emission and safety, then vocabulary and layout).
