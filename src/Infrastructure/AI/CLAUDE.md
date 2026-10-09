# 10.Intelligence — Domain Brain

> The AI abstraction and provider layer: text embeddings, tenant-scoped vector collections (upsert/delete/query/get/count/scroll against a closed metadata filter) and stateless chat/completion orchestration, behind `SharedKernel.AI.Abstractions`. Two sibling providers wire the vendors: `SharedKernel.AI.Qdrant` (the only vector database) and `SharedKernel.AI.SemanticKernel` (LLM orchestration and embeddings over OpenAI-compatible endpoints). Each provider declares the capabilities only it has in its own package, so a provider swap is a **build error**, not a startup error. Philosophy: **provider-swappable, model-identity-bound, cost-visible, fail-loud — never degrade.** This domain deliberately does not own tool execution or agent loops (the caller runs tools), retries or caching of completions, prompt sanitisation, corpus re-embedding, an LLM readiness probe, or any reference to Domain, Application, Persistence, Messaging, Search, Security or Caching.

## Packages

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.AI.Abstractions` | Abstractions | `IEmbeddingGenerator`, `IVectorCollection<TRecord>`, `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`, `ISemanticKernel`, `ICompletionProviderDescriptor`, `IVectorRecord`, the closed 8-node `VectorFilter` AST over the five-kind `VectorValue`, `VectorCollectionDefinition` (+ builder, fingerprint), request/result models, `VectorCollectionReadinessProbe`, `IntelligenceErrors`, `IntelligenceWellKnown`, `IntelligenceStreamException`. References `Primitives` and `Execution` only; **zero third-party packages**; no DI extension, no logging, no `ActivitySource`. |
| `SharedKernel.AI.Qdrant` | Adapter | Vector contracts over `Qdrant.Client` (gRPC); Qdrant-exclusive `IQdrantHybridQueryAccessor<TRecord>` (dense+sparse RRF), `IQdrantQuantizationProfileAccessor`, gated `IQdrantRawClientAccessor`; one readiness probe per collection. |
| `SharedKernel.AI.SemanticKernel` | Adapter | `IEmbeddingGenerator` (on `OpenAI.Embeddings.EmbeddingClient`), `ISemanticKernel` (on SK's `IChatCompletionService`), `ICompletionProviderDescriptor`; SK-exclusive `IKernelPluginAccessor`, gated `IKernelRawClientAccessor`; opt-in `WithBoundedRetry`. |
| `SharedKernel.AI.Testing` | Testing | In-memory doubles of every neutral contract. |

Also in the folder: `consumer-verify/Qdrant`, `consumer-verify/SemanticKernel` (one harness per provider, each unable to name the other's exclusive types).

## Public Entry Points

Options, defaults and examples: `SharedKernel.AI.Qdrant` README, `SharedKernel.AI.SemanticKernel` README.

- **Application code injects only** the six neutral contracts (`IEmbeddingGenerator`, `IVectorCollection<TRecord>`, `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`, `ISemanticKernel`, `ICompletionProviderDescriptor`) — never `QdrantClient`, `Kernel` or an `OpenAIClient` (`SK0026`).
- **Records:** implement `IVectorRecord` (`Id`, `Vector`, `ModelId`, `Metadata` of `VectorValue`).
- **Qdrant:** `services.AddSharedKernelQdrant(configuration)` (section `Intelligence:Qdrant`) → `.AddCollection<TRecord>(name, c => c.EmbeddingModel(modelId, dimension).DistanceMetric(...).TenantField(...).Field(...))`, optional `.AllowRawClientAccess()`, `.Build()`. Collections and the hybrid accessor are scoped; client, provisioner, descriptor and quantization accessor are singletons.
- **SemanticKernel:** `services.AddSharedKernelSemanticKernel(configuration)` (section `Intelligence:SemanticKernel`) → optional `.WithBoundedRetry(maxAttempts, baseDelay)`, `.AllowRawClientAccess()`, `.Build()`. The three contracts are singletons; the `OpenAIClient` is built once over a named `IHttpClientFactory` client.
- **Readiness:** one `VectorCollectionReadinessProbe` per collection, named `vector-store-{provider}-{collection}` (`ProbeNameFor`); mapped by `AddSharedKernelReadiness()`.
- **Telemetry:** `ActivitySource`/`Meter` name `SharedKernel.AI` (`IntelligenceWellKnown`); tag keys `ai.provider`, `ai.collection`, `ai.model` are declared and not yet emitted; subscribed by ServiceDefaults' `WithIntelligenceTelemetry()`.

## Rules & Invariants

1. **The seam rule:** `.Abstractions` holds no member a candidate provider cannot implement completely and correctly. A capability that would make one adapter throw, approximate or no-op becomes a provider-declared contract or is declined. Guard `VectorCollectionDefinition`/`VectorFieldDefinition` hardest (index type, quantization, consistency level, partitioning are engine claims).
2. **An embedding is meaningless without its model identity.** Validate collection `EmbeddingModelId`/`Dimension`/`DistanceMetric` against `IVectorRecord.ModelId`/`Vector.Length` on every write and `VectorQuery.ModelId`/`Vector.Length` on every query **before any I/O** (`EmbeddingModelMismatch`, `DimensionMismatch`, `DistanceMetricMismatch`). No engine detects a same-dimension, different-model mix.
3. **The distance metric is part of the contract**, declared once and folded into `Fingerprint`; never a per-query parameter.
4. **`TenantScope` is a mandatory, separate parameter** on every read and filtered write — never on `VectorQuery`, never inside the caller's `VectorFilter`. Adapters inject it as the outermost `AND` after translating the caller's filter, storing and matching the tenant as the `TenantId` `"D"` string. A tenant-declaring collection (`TenantField` set) given `TenantScope.Global` → `IntelligenceErrors.TenantScopeMissing`, no I/O.
5. **Never degrade.** No dropping, coercing or in-memory post-filtering of a clause the engine cannot express; every rejection (undeclared/non-filterable field, depth or batch ceiling, invalid record id) is a `Result` failure before I/O. No runtime capability-flag checks at call sites.
6. **Expected failures are `Result` values via `IntelligenceErrors`** — never ad-hoc `Error`s, never `Error.None`. Only `ScrollAsync` and `CompleteStreamingAsync` return a bare `IAsyncEnumerable<T>` (with `[EnumeratorCancellation]`) and throw `IntelligenceStreamException` mid-stream.
7. **Token usage is always returned** on `EmbeddingResult`, `EmbeddingBatchResult`, `CompletionResult` (accumulated across streaming chunks). `ValidateContextWindow` rejects an over-size request before dispatch.
8. **No retry, cache, tool-execution or agent member on `ISemanticKernel`.** `CompleteAsync` always dispatches fresh. The only retry is `.WithBoundedRetry(...)`: `CompleteAsync` only (never streaming), and only on a real HTTP 429 or 5xx status — never on the mapped `Error.Type`. Tools are offered with auto-invoke off; `ToolCallsRequested` returns to the caller.
9. **Never promise determinism**; never assert on generated text.
10. **Content is never logged or tagged:** `ChatMessage.Content`, `CompletionChunk.DeltaContent`, retrieved `Metadata` values, raw vectors, API keys. Log ids, model ids, token counts, latencies, outcome codes. Retrieved text is untrusted input; this layer is not a sanitizer and never adds hidden prompt enrichment.
11. **`Score` is provider- and metric-specific** (cosine bounded, dot unbounded, Euclidean smaller-is-better). Keep the loud XML docs on `VectorHit<TRecord>.Score`/`VectorQuery.MinScore`; `Rank` is the portable ordering.
12. **No write-consistency parameter.** Engines disagree on write visibility; `WaitUntilQueryableAsync` is the explicit barrier (a documented no-op on Qdrant, whose writes use `wait: true`).
13. **Siblings never reference each other** and never expose another SDK's types (`IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther`). Provider-exclusive contracts live only in their provider package.
14. **Raw-client hatches are gated:** registered only after `.AllowRawClientAccess()`, which logs a startup Warning, with an XML doc stating in capitals that the raw client **BYPASSES TENANT SCOPING** (SemanticKernel: bypasses the package's retry/observability seam).
15. **One registration per `TRecord`** — a second unkeyed `AddCollection<TRecord>` silently wins.
16. **Qdrant record ids** must be an unsigned 64-bit integer string or a `"D"` UUID; `QdrantRecordMapper.ToPointId` rejects anything else as `InvalidRecordId` before I/O.
17. **The Qdrant fingerprint needs server v1.16.0+** (collection `metadata`); older servers silently drop it. The metadata overloads exist only on the concrete `QdrantClient`; everything else goes through `IQdrantClient`.
18. **`QdrantFilterCompiler` switches exhaustively with no discard arm** (`CS8509;CS8524` kept visible as `WarningsNotAsErrors`); a new filter node must be translated, never ignored.
19. **Readiness:** vector probes only; ready = reachable, addressable and queryable (a write backlog never fails it). No `IHealthCheck` and no `Microsoft.Extensions.Diagnostics.HealthChecks` reference anywhere here (`IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages`). No LLM probe (it would be a billed call).
20. **No reflection in this domain's own code** (`Activator.CreateInstance`, `MakeGenericMethod`/`MakeGenericType`, `dynamic`; `SK0012` does not catch `MakeGenericType`). SemanticKernel's internal reflection stays inside its package; `ToolDefinition.ParametersJsonSchema` is a string to keep it contained.
21. **Collection, field and model identifiers are named constants** (`SK0027`); tag keys come from `IntelligenceWellKnown`. No `new HttpClient()`; no static mutable state.
22. A type constructed from raw `TOptions` must be registered through a factory unwrapping `IOptions<TOptions>.Value` — `AddValidatedOptions` registers only `IOptions<T>`.
23. **`EnsureCollectionAsync` is safe to run from every replica at once.** A create that loses the race (gRPC `AlreadyExists`) continues as on an existing collection: fingerprint check, then any missing payload index.
24. *(planned, SK.10.IntelligenceObservability)* **`Error.Message` holds kernel-authored wording and bounded identifiers only:** provider, operation, collection and model names, the caller's own record id, numbers, an HTTP status or gRPC status name. Never an exception message, `RpcException.Status.Detail`, a response body, an endpoint or host, a filter value, a key or prompt text; the detail reaches the log through the exception parameter. `reason` parameters of `IntelligenceErrors` are kernel-authored text.
25. *(planned, SK.10.IntelligenceObservability)* **Classify-or-rethrow, caller's token first.** Only a recognised provider fault becomes a `Result` failure (exception filters, so anything else is not caught and keeps its stack). The caller's cancellation always surfaces as `OperationCanceledException`, even when the transport reports it as another exception type (gRPC `Cancelled`); a transport or HTTP-client timeout with the caller's token live is `intelligence.timeout`. Outage and throttling are `ErrorType.Unavailable`, timeouts `Timeout`, with the same code and type for the same condition on every provider. Classified failures log once, at Warning, with the exception; propagating exceptions are not logged here.
26. *(planned, SK.10.IntelligenceObservability)* **`IntelligenceStreamException` is a sealed `SharedKernelException`** (so the edges map it by its `ErrorType`); `.Abstractions` therefore references `SharedKernel.Core` and nothing third-party.
27. *(planned, SK.10.IntelligenceObservability)* **Every embedding, vector and completion call emits one Client span `ai {operation}` and one `sharedkernel.ai.operation.duration` observation (unit `s`); token usage returned by the provider is counted in `sharedkernel.ai.tokens` (unit `{token}`).** Tags are bounded configuration only (`ai.provider`, `ai.operation`, `ai.collection`, `ai.model`, `outcome`); content, ids, vectors, filter values, keys and hosts are never tags (rule 10). Failures are recorded only through the shared recorder; cancellation is never an error. `CompleteAsync` and `EmbedManyAsync` dispatch exactly once unless `WithBoundedRetry` is configured (rule 8), including at the SDK level.
28. *(planned, SK.10.IntelligenceConformance)* Codes are public constants in `IntelligenceErrorCodes` (lowercase `intelligence.{snake_reason}`; reason words `unavailable`, `not_supported`); `IntelligenceErrors` stays public because both providers and the double construct it. A provider gets its own `…ErrorCodes` class only for a provider-exclusive code. The provider rejecting the kernel's own key is `Unexpected` (`intelligence.engine_unauthorized`); `tenant_scope_missing` stays `Unauthorized`.
29. *(planned, SK.10.IntelligenceConformance)* One `ActivitySource` and one `Meter` per provider assembly under the single name `SharedKernel.AI` (static source, `IMeterFactory` meter, `TelemetryVersion.Of`), named `{Provider}Telemetry`; every tag key, operation name and instrument name is a constant (`IntelligenceTagKeys`, `IntelligenceOperations`, `sharedkernel.ai.*`), identical for every provider. Control-plane calls (ensure, exists, delete, cutover, quantization profile) are spans too; the readiness probe is not.
30. *(planned, SK.10.IntelligenceConformance)* Each provider owns its EventId sub-block through an internal `…LoggingEventIds` class (`LoggingEventIdRanges.Intelligence + n`, no bare literal); `Diagnostics/` holds plain static partial `…Log` classes with L3 templates; loggers are `ILogger<KernelType>` and required. Programming errors use `UnreachableException`/`ThrowIf*`; configuration errors use E3 wording.

## Decisions

| Decision | Why |
|---|---|
| Sibling providers (`.Abstractions` + `.{Provider}`), no shared `.Core` | A vector database and an orchestration engine are different technologies; duplication between siblings is accepted. |
| Declined `Microsoft.Extensions.AI.Abstractions` in `.Abstractions` | Abstractions tier takes no third-party package; M.E.AI has no vector-store concept and no model-identity/dimension/metric binding. Providers may use it internally. |
| Declined a Milvus provider | `Milvus.Client` has no stable release; re-evaluate from scratch if a second vector provider is wanted. |
| Embeddings built on `OpenAI.Embeddings.EmbeddingClient`, not SK's embedding service | SK's `ITextEmbeddingGenerationService` returns no token usage; chat uses SK's `IChatCompletionService`, whose metadata carries usage and finish reason. |
| `Score` exposed (unlike `09.Search`) | RAG callers threshold on similarity; the scale caveat is documented instead. |
| `CutoverAsync` exists, re-embedding does not | Makes the need for a staging→live swap visible without owning an expensive rebuild. |
| No LLM readiness probe | The only honest check is a real, billed completion. |
| Provider-exclusive contracts in provider packages | A swap surfaces every non-portable call site at compile time (`CS0234`). |
| `ICompletionProviderDescriptor` and `IVectorProviderDescriptor` kept separate | Vector ceilings and LLM context/output ceilings share no honest members. |
| *(planned, SK.10.IntelligenceObservability)* `.Abstractions` references `SharedKernel.Core` so `IntelligenceStreamException` derives `SharedKernelException` | Edges map by `SharedKernelException`, so a non-deriving exception answers 500 and loses its `Error`. A Foundation reference is tier-legal and `Core` adds no third-party package; the earlier "must not reference Core" remark is superseded. |
| *(planned, SK.10.IntelligenceObservability)* Provider free text (exception message, gRPC detail, connector body, endpoint) never enters an `Error`; unclassified exceptions propagate instead of becoming `engine_fault`/`completion_failed` | The text repeats user input and infrastructure names and reaches ProblemDetails; a catch-all hid defects, mis-mapped cancellation and timeouts behind a soft failure. The detail is not lost: it reaches the log through the exception. |
| *(planned, SK.10.IntelligenceObservability)* `rate_limited` is `Unavailable`; there is no rate-limit `ErrorType` | The `ErrorType` contract classes a throttling dependency as `Unavailable` (HTTP 503). Revisit if 01.Core adds a distinct member. |
| *(planned, SK.10.IntelligenceObservability)* Instruments are created with their final `sharedkernel.ai.*` names; no `gen_ai.*` semantic-convention names; no span for the readiness probe or the no-op `WaitUntilQueryableAsync` | Nothing was emitted before, so no rename ever ships; the GenAI conventions are still experimental; the health request is already a span. |
| *(planned, SK.10.IntelligenceConformance)* `IntelligenceErrors` remains public although root rule C2 says the factory is internal; `engine_unauthorized` is `Unexpected` | Both providers and the double construct the same vocabulary from separate assemblies (an `InternalsVisibleTo` from Abstractions would name its siblings, rule 13). The provider refusing the kernel's own key is a deployment defect, not the end user's authentication. Flagged to `arch-lead` for a C2 amendment. |
| *(planned, SK.10.IntelligenceConformance)* "One telemetry class under `SharedKernel.AI`" means one name with per-provider instances and shared constants | `.Abstractions` may not own an `ActivitySource` and a shared instance would need an edge between siblings (rule 13). |

## Logging

Block **10000–10999** (`LoggingEventIdRanges.Intelligence`), 100-wide sub-blocks, written `LoggingEventIdRanges.Intelligence + n`:

| Package | Sub-block | In use |
|---|---|---|
| `AI.Abstractions` | 10000–10099 | Reserved, unused (no logging). |
| `AI.Qdrant` | 10100–10199 | `Logging/QdrantLog.cs`: +100 … +114. Next free +115. |
| (unclaimed) | 10200–10299 | — (reserved for the next vector provider) |
| `AI.SemanticKernel` | 10300–10399 | `Logging/SemanticKernelLog.cs`: +300 … +309. Next free +310. |

*(planned, SK.10.IntelligenceObservability)* No id is added or renumbered: 10100 drops host and port, 10106 and 10305 carry `{ErrorType}`/`{ErrorCode}` instead of `Error.Message`, 10111, 10113, 10307 and 10309 take the exception (10113 and 10307 become Warning and 10113 is also used by the provisioner and accessor catch sites). *(planned, SK.10.IntelligenceConformance)* `Logging/` moves to `Diagnostics/` with `…LoggingEventIds` constants and L3 templates; the numbers stay.

## Cross-Domain Couplings

- **01.Core:** `Result`/`Error`, `IReadinessProbe`, `LoggingEventIdRanges` (Primitives); `TenantScope`/`TenantId` (Execution); `AddValidatedOptions` (Configuration, providers only).
- **13.ServiceDefaults:** `WithIntelligenceTelemetry()` subscribes to `SharedKernel.AI` by name (no project reference to this domain); `AddSharedKernelReadiness()` maps the `vector-store-*` probes. *(planned, SK.13.WildcardTelemetry)* The `SharedKernel.*` wildcard replaces `WithIntelligenceTelemetry()`, and its seconds-histogram view covers `sharedkernel.ai.operation.duration`.
- **01.Core / 16.Testing / 00.Governance *(planned, WO-087)*:** the span failure recorder, `TelemetryVersion`, `IHasError` and the outcome constants come from `SharedKernel.Primitives`; tests assert telemetry and logs through the `SharedKernel.Testing.Telemetry` helpers; the report-only SK0043 and platform telemetry/EventId/template tests must show zero findings for this domain.
- **16.Testing:** a new neutral member needs its `SharedKernel.AI.Testing` double updated; `QdrantContainerFixture` (`qdrant/qdrant:v1.16.0`) lives in `SharedKernel.Testing.Internal`.
- **00.Governance:** `IntelligenceTopologyRules` (`AbstractionsHasNoThirdPartyDependencies`, `ProviderPackagesNeverReferenceEachOther`, `NoHealthChecksDependencyAcrossIntelligencePackages`); `SK0026` (raw provider client injection), `SK0027` (raw identifier literals).

## Testing

- **Unit lane:** `AI.Abstractions.Tests`, `AI.SemanticKernel.Tests`, `AI.Testing.Tests` and both `consumer-verify` harnesses. **Integration lane:** `AI.Qdrant.Tests` (real Qdrant via `QdrantContainerFixture`, shared with `[CollectionDefinition]` + `ICollectionFixture<T>`).
- **Never assert on generated text; never call a paid or live endpoint by default.** A live-model test, if any, is opt-in and environment-gated.
- Vector behaviour (filter translation, tenant injection) is proven against the real container, never a mocked client. The Qdrant conformance suite (`QdrantConformanceCollection`) is written so a future vector provider runs the same corpus and assertions (identity, count, filter matches — not `Score`). Corpus ids must be Qdrant-legal (numeric strings or UUIDs).
- **Fail-loud tests:** every rejection path asserts the `Error` **and** that no I/O occurred — with `Substitute.For<IQdrantClient>()` check `ReceivedCalls()` is empty, plus a companion guard-satisfied test asserting calls were made (an unconfigured substitute returns empty protobuf messages rather than throwing). Where no interface exists (`EmbeddingClient`), construct with a `null!` client and assert the distinguishing failure code, since the adapter maps any exception to a `Result`.
- Status-code → `Error` mapping may use NSubstitute or minimal `System.ClientModel.Primitives.PipelineResponse` subclasses — never reflection-fabricated SDK objects.
- When capturing an outbound request body through `FakeHttpMessageHandler`, read it inside the response factory; `System.ClientModel` disposes the content after the call.
- DI tests need no container: missing required options fail at start naming the property; exclusive contracts resolve only from their provider's builder; raw accessors resolve only after `.AllowRawClientAccess()`. Client construction is lazy, so consumer-verify needs no live server.
- `InternalsVisibleTo` exposes translators, validators and descriptors to each provider's own `.Tests`.
- Fakes: `SharedKernel.AI.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- `intelligence.unreachable`, `intelligence.timeout`, `intelligence.write_timeout` and `intelligence.rate_limited` are `ErrorType.Unexpected`; `Unavailable`/`Timeout` (HTTP 503/504) would be accurate *(closes with SK.10.IntelligenceObservability)*.
- The Qdrant and Semantic Kernel providers declare an `ActivitySource` and a `Meter` but emit no span or metric, token usage is not counted, provider exception text and the collection-as-endpoint reach `Error.Message`, `Error.Message` is logged, no log passes its exception, every non-cancellation exception becomes `engine_fault`/`completion_failed`, and `IntelligenceStreamException` is not a `SharedKernelException` *(closes with SK.10.IntelligenceObservability)*. The chat path may map `ClientResultException` only while Semantic Kernel throws `HttpOperationException`, and the OpenAI client may retry underneath `CompleteAsync` (both to be verified there).
- Qdrant is the only vector provider, so the conformance suite runs solo.
- `Microsoft.SemanticKernel` is reflection-heavy and not AOT-safe (contained in its package); `Qdrant.Client`'s trim behaviour is unverified.
- `SK0027`'s XML doc and `ProvisionerMethods` set still name a provisioner `ProbeAsync` that no longer exists on `IVectorCollectionProvisioner` (harmless; fix in `00.Governance`).
