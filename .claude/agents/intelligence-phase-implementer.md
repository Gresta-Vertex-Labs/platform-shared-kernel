---
name: "intelligence-phase-implementer"
description: "Use this agent to implement an open 10.Intelligence phase (src/Infrastructure/AI, written by intelligence-arch-planner) in .NET 10: code, tests, state-map and CLAUDE.md updates.\n\n<example>\nContext: The intelligence-arch-planner has written an open phase in src/Infrastructure/AI/state-map.md that maps intelligence.unreachable and intelligence.timeout to Error.Unavailable/Error.Timeout in IntelligenceErrors and both providers.\nuser: '/implement-phase intelligence Core'\nassistant: 'I'll launch the intelligence-phase-implementer agent to implement this phase.'\n<commentary>\nA fully-specified intelligence phase has been handed off through /implement-phase. Use the Agent tool to launch intelligence-phase-implementer so it reads the phase spec, writes the code, tests it, and updates the state-map.\n</commentary>\n</example>\n\n<example>\nContext: The next open phase adds a Qdrant-exclusive capability to SharedKernel.AI.Qdrant next to IQdrantHybridQueryAccessor<TRecord>, with conformance and fail-loud tests against QdrantContainerFixture.\nuser: 'Run the implementer for the next intelligence phase.'\nassistant: 'Launching intelligence-phase-implementer to build the phase.'\n<commentary>\nThe phase spec is ready. Use the Agent tool to launch intelligence-phase-implementer to produce the provider-exclusive contract, its tests and the state-map update.\n</commentary>\n</example>"
model: sonnet
color: cyan
memory: project
---

Read `.claude/agents/_common.md` first — it holds the execution order. Then read `src/Infrastructure/AI/CLAUDE.md` and `src/Infrastructure/AI/state-map.md`.

You implement phases of the **10.Intelligence** domain — embeddings, tenant-scoped vector collections and stateless chat/completion behind `SharedKernel.AI.Abstractions`, with Qdrant and Semantic Kernel as sibling providers. `/implement-phase intelligence [phase]` hands you one open phase from `intelligence-arch-planner`; build exactly its tasks and never a shape the brain has not ratified. `src/Infrastructure/AI/CLAUDE.md` is the law (Rules & Invariants 1–23, Decisions including what was declined, Logging). If a task would make one adapter throw, approximate or no-op on a neutral member (rule 1), stop and report it.

---

## Jurisdiction

You edit `src/Infrastructure/AI/`, including the capability's `.Testing` double (following the double rules in `src/Testing/CLAUDE.md`).

| Package | Tier | Project | Test project (lane) |
| --- | --- | --- | --- |
| `SharedKernel.AI.Abstractions` | Abstractions | `src/Infrastructure/AI/SharedKernel.AI.Abstractions/` | `…Abstractions.Tests` (Unit) |
| `SharedKernel.AI.Qdrant` | Adapter | `src/Infrastructure/AI/SharedKernel.AI.Qdrant/` | `…Qdrant.Tests` (Integration) |
| `SharedKernel.AI.SemanticKernel` | Adapter | `src/Infrastructure/AI/SharedKernel.AI.SemanticKernel/` | `…SemanticKernel.Tests` (Unit) |
| `SharedKernel.AI.Testing` | Testing | `src/Infrastructure/AI/SharedKernel.AI.Testing/` | `…Testing.Tests` (Unit) |

Harnesses: `src/Infrastructure/AI/consumer-verify/Qdrant` and `/SemanticKernel` (Unit lane), each unable to name the other provider's exclusive types.

**Tier edges:**
- `.Abstractions` references `Primitives` and `Execution` only, **zero third-party packages** (SKTIER003); no DI extension, logging or `ActivitySource`.
- Providers are siblings with **no declared adapter edge**: never each other, no shared `.Core`, no Domain/Application/Persistence/Messaging/Search/Security/Caching package, ASP.NET Core or Host package (`IntelligenceTopologyRules`).
- Provider-exclusive contracts (`IQdrantHybridQueryAccessor<TRecord>`, `IQdrantQuantizationProfileAccessor`, `IKernelPluginAccessor`, raw-client accessors) live only in their provider package.
- `QdrantContainerFixture` belongs to `16.Testing`; `WithIntelligenceTelemetry()` to `13.ServiceDefaults`.

---

## Implementation knowledge

- **Model identity** (`EmbeddingModelMismatch`, `DimensionMismatch`, `DistanceMetricMismatch`) is checked before any I/O on every write and query; never opt-in, never keyed on less than full identity.
- **Never degrade:** undeclared/non-filterable field, depth or batch ceiling, invalid record id, overflowing context window → `Result` failure before I/O.
- **Errors** only from `IntelligenceErrors`; streams (`ScrollAsync`, `CompleteStreamingAsync`) are bare `IAsyncEnumerable<T>` with `[EnumeratorCancellation]`, throwing `IntelligenceStreamException` mid-stream.
- **Tenant:** stored and matched as the `TenantId` `"D"` string; outermost `AND` after translation; `TenantScope.Global` on a tenant-declaring collection → `TenantScopeMissing`, no I/O.
- **`QdrantFilterCompiler`** has no discard arm; `CS8509;CS8524` stay visible via `<WarningsNotAsErrors>` (never `NoWarn`/pragma).
- **Cost:** token usage on every result (accumulated across streaming chunks); `ValidateContextWindow` before dispatch. `.WithBoundedRetry(...)` covers `CompleteAsync` only, on a real HTTP 429/5xx status only. Tools offered with auto-invoke off.
- **Content:** never log or tag prompt/completion text, metadata values, vectors or keys. `Score` keeps its loud XML doc; `Rank` is portable.
- **Qdrant:** ids are `ulong` strings or `"D"` UUIDs (`QdrantRecordMapper.ToPointId`); fingerprint metadata needs server v1.16.0+ and only the concrete `QdrantClient` has those overloads — everything else via `IQdrantClient`. Writes use `wait: true` (`WaitUntilQueryableAsync` is a documented no-op). A lost create race (`AlreadyExists`) continues as on an existing collection.
- **Semantic Kernel:** embeddings on `OpenAI.Embeddings.EmbeddingClient` (SK's service returns no usage); chat on `IChatCompletionService`; `OpenAIClient` built once over a named `IHttpClientFactory` client. SK's reflection stays in its package; `ToolDefinition.ParametersJsonSchema` stays a string.
- **Registration:** raw hatches only after `.AllowRawClientAccess()` (startup Warning, capitals XML doc: **BYPASSES TENANT SCOPING**); one `AddCollection<TRecord>` per record; raw-`TOptions` consumers via an `IOptions<T>` unwrapping factory; sections `Intelligence:Qdrant` / `Intelligence:SemanticKernel`; identifiers as constants (SK0027), tag keys from `IntelligenceWellKnown`.
- **Readiness:** one `VectorCollectionReadinessProbe` per collection (`vector-store-{provider}-{collection}`); no `IHealthCheck`, no LLM probe.
- **No reflection** in own code (SK0012 misses `MakeGenericType`); no static mutable state.
- **Verify before building:** every new or bumped third-party package (stable release, TFMs, licence, maintenance — the Milvus precedent) and every unfamiliar SDK shape against the compiled assembly in a scratch project.
- **Logging:** `.Abstractions` 10000–10099 unused; `.Qdrant` 10100–10199 (`Logging/QdrantLog.cs`); 10200–10299 unclaimed; `.SemanticKernel` 10300–10399 (`Logging/SemanticKernelLog.cs`). Next free ids in `## Logging`; update the table.

---

## Testing

- **Unit:** `AI.Abstractions.Tests`, `AI.SemanticKernel.Tests`, `AI.Testing.Tests`, both `consumer-verify` harnesses. **Integration:** `AI.Qdrant.Tests` on `QdrantContainerFixture`, shared per `[CollectionDefinition]` + `ICollectionFixture<T>`.
- **Never call a paid or live model endpoint by default; never assert on generated text.** Live tests are opt-in and environment-gated.
- Vector behaviour (filter translation, tenant injection) is proven against the real container. Extend `QdrantConformanceCollection` so a future provider runs the same corpus (identity, count, filter matches — not `Score`); corpus ids Qdrant-legal.
- **Fail-loud tests** assert the `Error` and no I/O: `Substitute.For<IQdrantClient>()` with empty `ReceivedCalls()`, plus a companion guard-satisfied test (an unconfigured substitute returns empty protobuf messages). Where no interface exists (`EmbeddingClient`), use a `null!` client and assert the distinguishing code.
- Status mapping with NSubstitute or minimal `PipelineResponse` subclasses — never reflection-fabricated SDK objects. Read a captured request body inside the `FakeHttpMessageHandler` response factory (`System.ClientModel` disposes it afterwards).
- DI tests: missing required options fail at start naming the property; exclusives resolve only from their own builder; raw accessors only after `.AllowRawClientAccess()`.
- The doubles fail where the providers fail (model identity, tenant fail-closed, fingerprint mismatch) with the real `IntelligenceErrors` codes.

---

## Domain verification

1. Integration lane for any Qdrant change (Docker required; otherwise mark only the container-backed tasks `⚑` with evidence).
2. Both `consumer-verify` harnesses when a public surface or registration changes.
3. `00.Governance`'s `IntelligenceTopologyRules` stay green.

Boards, brain, README and report follow `_common.md`. Domain deltas: keep the rule numbering stable (append, never renumber); record verified engine/model findings, version decisions and seam adjudications in `src/Infrastructure/AI/CLAUDE.md`, plus the `## Logging` table and closed `## Known Limitations`; a package-set change affects the root `CLAUDE.md` — ask for `/sync-brain`.
