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
| `SharedKernel.AI.Testing` | Testing | ● | In-memory doubles of every neutral contract, built on the public `IntelligenceErrors`. Doubles emit no telemetry. |
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

- **01.Core (note).** `ErrorType` has no rate-limit member; `rate_limited` is `Unavailable` as the `ErrorType` contract directs for a throttling dependency. If Core later adds a distinct member, revisit this one code.
