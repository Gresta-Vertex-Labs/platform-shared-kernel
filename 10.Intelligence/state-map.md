# 10.Intelligence — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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

Test doubles: in-memory embedding/vector/kernel doubles in `16.Testing/SharedKernel.AI.Testing`; `QdrantContainerFixture` in `SharedKernel.Testing.Internal`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.10.Design` | Design (D-01–D-17; WO-045 P-279–P-282, WO-047 P-291) | ● |
| `SK.10.Scaffold` | Scaffold (S-01–S-07; S-05 Milvus retracted) | ● |
| `SK.10.Core` | Core (C-01–C-11; C-06–C-08 Milvus retracted) | ● |
| `SK.10.Tests` | Tests (T-01–T-10; Milvus halves retracted) | ● |
| `SK.10.Docs` | Docs (DO-01–DO-05; DO-03 Milvus retracted) | ● |
| `SK.10.Published` | Published (P-01–P-06) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. Readiness probes (`IReadinessProbe`, mapped by `13.ServiceDefaults`' `AddSharedKernelReadiness()`), `WithIntelligenceTelemetry`, the `16.Testing` doubles and fixture, and `SK0026` (raw `QdrantClient`/`Kernel` injection) have all shipped.

## Completed Phases

- WO-086 ● Foundation refactor — `TenantScope` from `SharedKernel.Execution`, tenant field written/filtered as `TenantId`, probes, fakes moved to `SharedKernel.AI.Testing` (P-564–P-575) (2026-09-26)
- WO-048 ● `SharedKernel.AI.Milvus` retracted; domain closes fully ● (2026-07-27)
- WO-047 ● D-17 — Invariant #8 reconciled with `ICompletionProviderDescriptor` (P-291) (2026-07-27)
- SK.10.Published ● Packed clean; `consumer-verify.Qdrant` / `.SemanticKernel` harnesses (2026-07-24)
- SK.10.Docs ● READMEs and NuGet metadata (2026-07-24)
- SK.10.Tests ● 276/276 across the three packages (2026-07-24)
- SK.10.Core ● Qdrant and SemanticKernel adapters (2026-07-24)
- SK.10.Scaffold ● `.VectorDb` retired, tests re-homed, provider projects created (2026-07-22)
- SK.10.Design ● Shape C split ratified; `Microsoft.Extensions.AI.Abstractions` adoption declined (WO-045) (2026-07-21)

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] WO-086 foundation refactor (P-564–P-575) recorded.
- [2026-07-27] WO-048 — `SharedKernel.AI.Milvus` retracted; all six phases ●.
- [2026-07-27] WO-047 / P-291 — documentation-only Design correction (D-17).
- [2026-07-24] SK.10.Published — three packages packed clean; consumer-verify harnesses added.
