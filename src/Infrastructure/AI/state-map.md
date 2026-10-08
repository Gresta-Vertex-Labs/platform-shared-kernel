# 10.Intelligence — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.AI.Abstractions` | Abstractions | ● | Zero third-party. Embedding generation (validates model identity, dimension, metric before I/O), vector collections with a mandatory `TenantScope` (from `SharedKernel.Execution`), `ISemanticKernel`-shaped orchestration (token usage is a result, no silent completion retry, prompt text never logged). |
| `SharedKernel.AI.Qdrant` | Adapter | ● | The only vector provider (`Qdrant.Client`). Qdrant-exclusive contracts (quantization profile, hybrid query) stay in this package; one `vector-store-{provider}-{collection}` probe per collection. Verified by `consumer-verify.Qdrant` and a real-Testcontainers conformance suite. |
| `SharedKernel.AI.SemanticKernel` | Adapter | ● | LLM orchestration and embeddings over Microsoft.SemanticKernel; `IKernelPluginAccessor`. No LLM readiness probe by design (a probe would be a billed completion call). Verified by `consumer-verify.SemanticKernel`. |
| `SharedKernel.AI.Milvus` | — | ⊘ | Retracted by WO-048 (2026-07-27): `Milvus.Client` never shipped a stable release. Never built. |
| `SharedKernel.AI.VectorDb` | — | ⊘ | Retired at Scaffold S-01 (replaced by the Abstractions + provider split). |

Test doubles: in-memory embedding/vector/kernel doubles in `src/Infrastructure/AI/SharedKernel.AI.Testing`; `QdrantContainerFixture` in `SharedKernel.Testing.Internal`.

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. Readiness probes (`IReadinessProbe`, mapped by `13.ServiceDefaults`' `AddSharedKernelReadiness()`), `WithIntelligenceTelemetry`, the `16.Testing` doubles and fixture, and `SK0026` (raw `QdrantClient`/`Kernel` injection) have all shipped.
