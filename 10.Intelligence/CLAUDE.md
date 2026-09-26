# 10.Intelligence — AI / Vector Retrieval Brain

## What This Domain Is

The AI abstraction and provider-wiring layer. Downstream microservices depend on `SharedKernel.AI.Abstractions` to generate text embeddings, upsert/delete/query vectors in a vector database against a structured metadata filter, and run chat/completion (including streaming and tool-calling) LLM orchestration — never on a concrete model SDK or vector-database client. Two sibling provider packages — `SharedKernel.AI.Qdrant` (vector database), `SharedKernel.AI.SemanticKernel` (LLM orchestration) — wire the vendor client, translate the neutral models onto the provider's own surface, and own all provider-specific configuration. Each provider additionally declares — **inside its own package, never in `.Abstractions`** — the typed contracts for the capabilities only that provider genuinely has, so a provider swap is a **compile error**, not a startup resolution error (the `09.Search` precedent).

Philosophy: **Provider-swappable. Model-identity-bound. Cost-visible. Fail-loud, never degrade.**

> **Tiers:** `SharedKernel.AI.Abstractions` is Abstractions tier and references only `SharedKernel.Primitives` and `SharedKernel.Execution` (Foundation — `TenantScope`/`TenantId`). `SharedKernel.AI.Qdrant` and `SharedKernel.AI.SemanticKernel` are Adapter tier (plus `SharedKernel.Configuration`) and never reference each other. No package here references `SharedKernel.Domain`, the application pipeline, persistence, messaging, search, security or caching. The build enforces the tiers (SKTIER001–006). `SharedKernel.AI.Abstractions` contains **no type that a candidate provider cannot implement completely and correctly** — if implementing a member would require one adapter to throw, degrade, approximate, or no-op, that member does not belong in `.Abstractions`. That is the same intersection-only seam rule `09.Search` proved out, applied to a domain where the failure mode is worse: a degraded search returns fewer rows, a degraded similarity query returns **confidently wrong** rows with no error.

---

## Status

The interface contracts below are the current, shipped state; they are not to be renegotiated without a new work order. All three packages are released with the repo-wide release train. Test doubles for every neutral contract ship in `16.Testing/SharedKernel.AI.Testing`; the Qdrant container fixture lives in the non-packable `16.Testing/SharedKernel.Testing.Internal`. How the domain got here — the Milvus retraction (WO-048), the `ProbeAsync` retraction on `ICompletionProviderDescriptor` (WO-047), the real-container defects fixed during `SK.10.Tests`, and the WO-086 refactor (shared `TenantScope`, `IReadinessProbe`) — is recorded in `state-map.md` ("Domain-Brain Changelog").

---

## Packages

| Package | Tier | Role |
| --- | --- | --- |
| `SharedKernel.AI.Abstractions` | Abstractions | `IEmbeddingGenerator`, `IVectorCollection<TRecord>`, `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`, `ISemanticKernel`, `ICompletionProviderDescriptor`, the closed 8-node `VectorFilter` AST, `VectorValue`, `VectorCollectionDefinition` + builder, the request/result models, `VectorCollectionReadinessProbe`, `IntelligenceWellKnown`, `IntelligenceErrors`, `IntelligenceStreamException` — the only types application code should ever inject or construct. Tenant scoping uses `SharedKernel.Execution.Tenancy.TenantScope`; this package declares no tenant type of its own. Ships **no** DI extension, **no** `ActivitySource`, **no** `[LoggerMessage]`, **no** `IHealthCheck` |
| `SharedKernel.AI.Qdrant` *(vector database)* | Adapter | Concrete Qdrant implementation of `IVectorCollection<TRecord>`, `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`; registers one `vector-store-qdrant-{collection}` readiness probe per collection. Additionally **declares** Qdrant-exclusive contracts (sparse/hybrid vectors, quantization profile access, `IQdrantRawClientAccessor`) |
| `SharedKernel.AI.SemanticKernel` *(LLM orchestration)* | Adapter | Concrete `Microsoft.SemanticKernel`-backed implementation of `IEmbeddingGenerator`, `ISemanticKernel`, `ICompletionProviderDescriptor`. Additionally **declares** SemanticKernel-exclusive contracts (plugin/planner access, `IKernelRawClientAccessor`) |

The package split is **sibling providers** (`.Abstractions` + `.{Provider}` leaves, no shared `.Core`), matching `08.Storage` and `09.Search`. The `.{Provider}.{Role}` shape is reserved for one technology serving several roles (the `02.Caching` Redis family); a vector database and an orchestration engine are different technologies serving different roles. A future second vector-database provider joins as another sibling against the same `.Abstractions` surface. **Sibling packages never reference each other**, in any direction; duplication between them (options-validation flow, filter-walker skeleton, receipt mapping) is deliberate. The capability token in package names is **`AI`**, not `Intelligence`.

`SharedKernel.AI.Milvus` was evaluated and retracted (WO-048): `Milvus.Client` never shipped a stable release. If a second vector provider is wanted, re-check that package from scratch.

### `Microsoft.Extensions.AI.Abstractions` — declined for `.Abstractions`

`SharedKernel.AI.Abstractions` takes **no** third-party `PackageReference`; `IEmbeddingGenerator`, `ISemanticKernel` and the vector-collection surface are this domain's own types. Reasons, so this is not re-litigated by accident: (1) no `.Abstractions` package on this platform takes a third-party package (SKTIER003 enforces the allow-list); (2) M.E.AI has no vector-database concept at all, so it would cover only half the surface; (3) it carries no mandatory model-identity/dimension/metric binding, which is this domain's sharpest invariant. Provider packages may use M.E.AI internally (SemanticKernel's own connectors do).

---

## Technology Stack

| Concern | Technology | Notes |
| --- | --- | --- |
| AI abstractions | Pure C# interfaces + `sealed record` / `readonly record struct` models | Zero third-party NuGet dependencies |
| Outcome type | `Result<T>` / `Result` / `Error` from `SharedKernel.Primitives` | Expected failures (model not found, rate limited, context-window exceeded, dimension mismatch, model-identity mismatch, collection not found, tenant scope missing) are `Error` values from `IntelligenceErrors`, never thrown exceptions. `Error` also has `Unavailable` and `Timeout` (added by P-562 for outages, HTTP 503/504); this domain's `intelligence.unreachable`/`intelligence.timeout` are still `Unexpected` — moving them is a P-562 follow-up |
| Tenant scope | `SharedKernel.Execution.Tenancy.TenantScope` over `TenantId` | The platform's one tenant scope; adapters write and filter the tenant field as the `TenantId`'s `"D"` string |
| Readiness | `SharedKernel.Primitives.Health.IReadinessProbe` | `VectorCollectionReadinessProbe`, one per registered collection, named `vector-store-{provider}-{collection}`; the host maps every probe with `AddSharedKernelReadiness()` |
| LLM orchestration | `Microsoft.SemanticKernel` `1.78.0` (+ explicit `Microsoft.SemanticKernel.Connectors.OpenAI`, `OpenAI`, `System.ClientModel`, `Microsoft.Extensions.Http` pins) | Reflection-heavy, non-AOT-safe; isolated in `SharedKernel.AI.SemanticKernel`. SK's `ITextEmbeddingGenerationService` carries **no token usage**, so `SemanticKernelEmbeddingGenerator` is built on `OpenAI.Embeddings.EmbeddingClient` directly (keeps Invariant #5 honest); the orchestrator uses SK's `IChatCompletionService`, whose `Metadata["Usage"]`/`["FinishReason"]` do carry it |
| Vector DB | `Qdrant.Client` `1.18.1` (+ explicit `Grpc.Core.Api`, `Google.Protobuf` pins) | Collection-level `metadata` persists `VectorCollectionDefinition.Fingerprint` and **requires Qdrant server `v1.16.0`+** — older servers silently drop it. The metadata-accepting `CreateCollectionAsync`/`UpdateCollectionAsync` overloads exist only on the concrete `QdrantClient`; everything else depends on `IQdrantClient` (the `NSubstitute` seam). Reflecting a client SDK proves nothing about the deployed server — only a real round-trip does |
| Configuration | `SharedKernel.Configuration.AddValidatedOptions<TOptions>` | Every provider options type is bound and validated at startup |
| DI composition | Per-provider `AddSharedKernel{Provider}...()` returning a fluent builder | See DI Registration below. `SharedKernel.AI.Abstractions` ships **no** DI extension |
| Logging | `[LoggerMessage]`, `EventId`s in **10000–10999** (`LoggingEventIdRanges.Intelligence`) | Abstractions **10000–10099** (reserved, unused), Qdrant **10100–10199**, SemanticKernel **10300–10399**; **10200–10299** is unclaimed |
| Diagnostics | Each provider declares its **own** `internal` `ActivitySource`/`Meter` named from `IntelligenceWellKnown.ActivitySourceName` / `.MeterName` (both `"SharedKernel.AI"`) | `13.ServiceDefaults`' `WithIntelligenceTelemetry()` wires them by string name, with no `ProjectReference` to this domain |
| Test doubles | `16.Testing/SharedKernel.AI.Testing` | `InMemoryEmbeddingGenerator` (deterministic hash-derived vectors), `InMemoryVectorCollection<TRecord>`, `InMemoryVectorCollectionProvisioner`, `InMemoryVectorProviderDescriptor`, `InMemorySemanticKernel`, `InMemoryCompletionProviderDescriptor` |
| Testing containers | `Testcontainers.Qdrant` | `QdrantContainerFixture` (image `qdrant/qdrant:v1.16.0`) in `16.Testing/SharedKernel.Testing.Internal`, consumed via `[CollectionDefinition]` + `ICollectionFixture<T>`, never hand-rolled inside a `.Tests` project |
| Test packages | `xunit`, `FluentAssertions`, `NSubstitute` | The repo-wide set from `Directory.Packages.props` |
| XML docs / packaging | `GenerateDocumentationFile` + `TreatWarningsAsErrors` + `PackageReadmeFile`/README pair | Package metadata and the version come from the repo-wide build props and release train |

---

## Domain Invariants

These are the load-bearing rules that make this domain different from every other capability domain in the repo. Binding now; the ratified Interface Contracts below implement each one by name.

### 1. An embedding is meaningless without its model identity

A vector is only comparable against vectors produced by the **same model, at the same dimensionality, with the same normalization**. Mixing two embedding models inside one collection produces similarity scores that are numerically valid, confidently typed, and **completely wrong** — with no error from any engine, ever.

Implemented by: `VectorCollectionDefinition.EmbeddingModelId` + `.Dimension`, validated against `IVectorRecord.ModelId` + `.Vector.Length` on every write, and against `VectorQuery.ModelId` + `.Vector.Length` on every query — **before any I/O**, returning `IntelligenceErrors.EmbeddingModelMismatch` / `.DimensionMismatch`. Dimension mismatch is the cheap half (the engine will often reject it too); **model-identity mismatch is the dangerous half**, because no engine can detect it — carrying `ModelId` explicitly on both the record and the query is what converts an undetectable silent-wrongness class into a fail-loud one.

Re-embedding an entire corpus on a model change is a real, expensive operation. `IVectorCollectionProvisioner.CutoverAsync` makes the **need** visible (a staging→live swap after a re-embed) without owning the rebuild itself — the same `IIndexRebuilder`-stays-out reasoning `09.Search` applied.

### 2. Distance metric is part of the contract, not a tuning knob

Cosine, dot-product, and Euclidean are not interchangeable. `VectorCollectionDefinition.DistanceMetric` is declared once, alongside model identity and dimension, and folded into `Fingerprint`. It is never a per-query parameter.

### 3. Tenant scope is a mandatory, non-nullable, non-defaulted separate parameter

The same `SharedKernel.Execution.Tenancy.TenantScope` and the same rule as `09.Search`. `TenantScope` is a required parameter on every `IVectorCollection<TRecord>` read and filtered write — never a member of `VectorQuery` or any request object, never routed through the caller-supplied `VectorFilter`. The adapter injects it as the **outermost conjunction** after translating the caller's filter. Fail closed: a tenant-declaring collection (`VectorCollectionDefinition.TenantField` set) queried or filter-written with `TenantScope.Global` returns `IntelligenceErrors.TenantScopeMissing` and performs **no I/O**.

### 4. Non-determinism is a property of the contract, not a defect to hide

Completion output is non-deterministic across calls even at temperature zero. `ISemanticKernel` never promises reproducibility, never caches a completion in a way that silently converts a fresh call into a stale one without the caller opting in, and is never tested by asserting on generated text (see Test Rules). Embedding generation is more stable but still model-version-bound — not a pure function across a provider's model upgrades.

### 5. Cost and token usage are first-class outputs, never hidden

Every completion and embedding call spends real money and consumes a finite context window. `TokenUsage` rides on `EmbeddingResult`, `EmbeddingBatchResult`, and `CompletionResult` unconditionally, and is accumulated across `CompleteStreamingAsync`'s chunks. `.Abstractions` exposes **no retry-shaped member anywhere** — a retry policy on a completion **re-bills**; if a provider package offers one at all, it is an explicit, bounded, opt-in builder call (e.g. `.WithBoundedRetry(...)`), never automatic. `ICompletionProviderDescriptor.ValidateContextWindow` is the pre-dispatch `Result` failure carrying the limit and the actual size, detected wherever the provider exposes enough information to detect it before dispatch.

### 6. Prompt and completion content is untrusted, sensitive, and never logged by default

Two independent hazards, both structural:

- **Outbound**: sending a payload to a hosted model endpoint is an outbound transfer of whatever that payload contains, to a third party, potentially across a data-residency boundary. The package neither classifies nor redacts caller content; it must make the transfer explicit and must never add hidden enrichment to a prompt.
- **Inbound**: text retrieved from a vector store and interpolated into a prompt is **untrusted input** — the prompt-injection class. This layer provides plumbing, not a sanitizer, and must never claim to be one.

Consequently `ChatMessage.Content`, `CompletionChunk.DeltaContent`, retrieved `IVectorRecord.Metadata` values, and `IVectorRecord.Vector` / `VectorQuery.Vector` are **never** log-message parameters. Log identifiers, model ids, token counts, latencies, and outcome codes. API keys and endpoint credentials live in options bound through `AddValidatedOptions` and are never logged, never echoed into an `Error` message, and never included in a diagnostic tag.

### 7. Streaming reads are not `Result`-wrapped

`IVectorCollection<TRecord>.ScrollAsync` and `ISemanticKernel.CompleteStreamingAsync` both return `IAsyncEnumerable<T>` **directly**, following the established `06.Persistence` P-149 / `08.Storage` P-265 / `09.Search` precedent. Mid-stream faults surface as `IntelligenceStreamException` from `MoveNextAsync`. Both apply `[EnumeratorCancellation]` to the token parameter.

### 8. Readiness is an `IReadinessProbe` per collection; this domain ships no `IHealthCheck`

Each vector provider's builder registers one `VectorCollectionReadinessProbe` (`SharedKernel.Primitives.Health.IReadinessProbe`) per registered collection, named `vector-store-{provider}-{collection}` (`VectorCollectionReadinessProbe.ProbeNameFor`), over the provider's internal collection measurement (`VectorCollectionHealth`). Ready = reachable **and** addressable **and** queryable; a write backlog never fails readiness. The measured values ride on `ReadinessReport.Data` under the probe's declared keys. `IVectorCollectionProvisioner` has **no** probe member. `10.Intelligence` ships **no** `IHealthCheck` implementation and references `Microsoft.Extensions.Diagnostics.HealthChecks` **nowhere**; the host maps every probe with `services.AddHealthChecks().AddSharedKernelReadiness()` (`SharedKernel.ServiceDefaults`).

**The LLM side has no readiness probe, by design.** `ICompletionProviderDescriptor` is a singleton, zero-I/O descriptor (`ProviderName`, `ContextWindowTokens`, `MaxOutputTokens`, `ValidateContextWindow`); the only honest way to check an LLM endpoint's reachability is a real, billed completion call — exactly the automatic/hidden, re-billing behavior Domain Invariant #5 forbids.

---

## Interface Contracts

> **RATIFIED (WO-045, P-279; tenant scope and readiness updated by WO-086).** This is the locked, member-by-member surface of `SharedKernel.AI.Abstractions`. Every deliberate omission is recorded with its reason so a future reviewer cannot re-litigate a settled decision. `CancellationToken cancellationToken = default` is the trailing parameter on every async member, matching `IFileStorage`/`ISearchIndex<TDocument>`.
>
> **A reading note on the many "Qdrant and Milvus"/"both engines" references below:** this contract was designed and validated at Design time (2026-07-21) against **two** vector-database engines' real filter grammars, write models, and provisioning primitives — Qdrant and Milvus — precisely so no single-engine assumption would sneak into a "neutral" surface. `SharedKernel.AI.Milvus` was later retracted as a shipping provider (WO-048, 2026-07-27; see **Packages** and **Technology Stack**) because `Milvus.Client` never shipped a stable release — an implementation-viability failure, not a design failure. The two-engine analysis that shaped every NOTE below remains accurate design history and is left as originally written rather than scrubbed; it is not a claim that a Milvus package exists or is coming.

### Vector record and scalar value (`Models/`)

```text
VectorValueKind   (enum)
    String = 0, Int64 = 1, Double = 2, Boolean = 3, DateTimeOffset = 4

VectorValue   (readonly record struct — a CLOSED scalar union, no `object`, no `dynamic`)
    .Kind                                                              → VectorValueKind { get; }
    .AsString / .AsInt64 / .AsDouble / .AsBoolean / .AsDateTimeOffset   (kind-checked accessors)
    .From(string) / .From(long) / .From(double) / .From(bool) / .From(DateTimeOffset)  → VectorValue
    .From(Guid value)                                                            → VectorValue
    — implicit operators from string, int, long, double, bool, DateTimeOffset, Guid
    — explicit, Kind-aware `public override string ToString()` (never throws — the SearchValue
      ToString defect precedent from 09.Search is avoided by design from day one, not fixed later)

    NOTE (DELIBERATE STRUCTURAL TWIN OF SearchValue): same five kinds, same accessor shape, same
          .From(Guid) canonical-"D"-string normalisation. Both Qdrant payload values and Milvus scalar
          fields are faithfully representable by exactly these five kinds — no engine asymmetry
          motivates a different union here, and reusing the proven shape avoids inventing a new defect
          class this domain would otherwise have to discover independently.

    NOTE (TIMESTAMP ENCODING IS A PROVIDER CONCERN): DateTimeOffset values are serialised to whatever
          each provider's own numeric/string convention requires (Qdrant payload: RFC 3339 string;
          Milvus: epoch-microseconds INT64, matching its own timestamp convention) — decided and
          implemented per-provider at Core phase, exactly mirroring 09.Search's per-provider timestamp
          divergence. The divergence is invisible to the caller and must be matched by whatever wrote
          the metadata field originally.

IVectorRecord   (the self-supplied surface every TRecord implements — no reflection, no attribute scan,
                 mirrors ISearchDocument / ILoggableRequest<TResponse> / ICacheableQuery.CacheKey)
    .Id                                                                          → string { get; }
    .Vector                                                          → ReadOnlyMemory<float> { get; }
    .ModelId                                                                     → string { get; }
    .Metadata                                    → IReadOnlyDictionary<string, VectorValue> { get; }

    NOTE: Every TRecord in this domain is constrained `where TRecord : class, IVectorRecord`.
          ModelId is the embedding-model identity that produced Vector — carried on the RECORD
          ITSELF (not inferred from Vector.Length, which cannot distinguish two different models that
          happen to share a dimension) so the adapter can validate it against
          VectorCollectionDefinition.EmbeddingModelId before any I/O. This is the direct implementation
          of Domain Invariant #1's "no engine can detect a model-identity mismatch" problem — the
          contract detects it instead.

    NOTE (Id STABILITY AND CHARSET): Id must be stable and identical across re-embeds — it is the
          upsert key on both engines. Charset is the intersection of both engines' point/entity-id
          constraints; a violating id returns IntelligenceErrors.InvalidRecordId before any I/O on BOTH
          providers (exact charset confirmed and documented at each provider's Core phase against its
          real, current API — not guessed at Design time, since Qdrant point IDs accept UUID or
          unsigned integer natively while Milvus primary keys are INT64 or VARCHAR, and the neutral Id
          here is always string, so each provider's translator owns the concrete validation).

    NOTE (Metadata IS THE ENTIRE PORTABLE PAYLOAD SURFACE): there is no separate "content" or "text"
          member — whatever text was embedded to produce Vector, if the caller wants it retrievable, is
          stored as a Metadata entry like any other field. This keeps IVectorRecord's shape uniform
          across every use case (RAG chunk, entity embedding, image caption embedding) rather than
          privileging one.
```

### Embedding generation (`Abstractions/`)

```text
TokenUsage   (sealed record — shared between embedding and completion results)
    .PromptTokens                                                                  → int { get; init; }
    .CompletionTokens                                                              → int { get; init; }
    .TotalTokens                                                                   → int { get; init; }

    NOTE: CompletionTokens is always 0 on an embedding result — embedding calls have no completion
          half. TotalTokens is NOT re-derived by a consumer; the provider reports it directly, since
          some providers bill on values that are not a pure sum (e.g. cached-prefix pricing).

EmbeddingResult   (sealed record)
    .Vector                                                          → ReadOnlyMemory<float> { get; init; }
    .ModelId                                                                       → string { get; init; }
    .Dimension                                                                        → int { get; init; }
    .TokenUsage                                                              → TokenUsage { get; init; }

EmbeddingBatchResult   (sealed record)
    .Embeddings                                     → IReadOnlyList<ReadOnlyMemory<float>> { get; init; }
    .ModelId                                                                       → string { get; init; }
    .Dimension                                                                        → int { get; init; }
    .TokenUsage                                                              → TokenUsage { get; init; }

    NOTE (ORDER-PRESERVING, NO PER-ITEM FAILURE SURFACE — unlike SearchBulkReceipt, deliberately):
          Embeddings[i] corresponds to the i-th input text. Embedding-provider batch APIs are atomic
          per request (the whole call embeds every item or the call fails), unlike a search engine's
          _bulk endpoint which routinely partial-fails. A Result<EmbeddingBatchResult> failure is
          therefore sufficient; there is no EmbeddingItemFailure type. A provider that genuinely offers
          native partial-batch failure exposes it as a provider-exclusive contract, never faked here.

IEmbeddingGenerator   (NOT generic — this domain is scoped to text embedding for retrieval/RAG, not
                       multi-modal embedding; this is the deliberate deviation from
                       Microsoft.Extensions.AI's IEmbeddingGenerator<TInput,TEmbedding> and is why that
                       type was not adopted verbatim even informally)
    .ModelId                                                                       → string { get; }
    .Dimension                                                                        → int { get; }
    .EmbedAsync(string text, CancellationToken ct)                    → Task<Result<EmbeddingResult>>
    .EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken ct) → Task<Result<EmbeddingBatchResult>>

    NOTE: ModelId/Dimension are zero-I/O properties bound at construction (from options), mirroring
          ISearchIndex.IndexName — cheap enough to read at composition time to validate a
          VectorCollectionDefinition.EmbeddingModelId/.Dimension pairing before any embedding call is
          ever made. A mismatch discovered here is exactly the kind of "fail at composition time, not
          query time" the provider-descriptor Validate members exist to make actionable elsewhere too.

    NOTE (BATCH SIZE IS A PROVIDER-DESCRIPTOR CONCERN, NOT HERE): EmbedManyAsync does not itself cap
          texts.Count — over-ceiling batches are IVectorProviderDescriptor... actually embedding batch
          ceilings belong to whichever provider's own descriptor covers it (SemanticKernel package);
          the adapter validates against its own known ceiling before any I/O and returns
          IntelligenceErrors.BatchSizeExceeded.
```

### Vector collection — write surface (`Abstractions/`)

```text
VectorWriteReceipt   (sealed record)
    .CollectionName                                                                 → string { get; init; }
    .ProviderToken                                                                  → string { get; init; }
    .AffectedCount                                                                     → int { get; init; }
    .AcceptedAt                                                              → DateTimeOffset { get; init; }

    NOTE: AcceptedAt sourced from IClock (01.Core) in both adapters — never DateTimeOffset.UtcNow
          (SK0001). ProviderToken is OPAQUE (a Qdrant operation id, or a Milvus insert timestamp used
          as a guarantee_timestamp) — consumers must NEVER parse it; its only legal use is being handed
          back to WaitUntilQueryableAsync on the SAME IVectorCollection<TRecord> instance.

VectorItemFailure   (sealed record)
    .RecordId                                                                       → string { get; init; }
    .Error                                                                          → Error { get; init; }

VectorBulkReceipt   (sealed record)
    .Receipt                                                        → VectorWriteReceipt { get; init; }
    .SucceededCount                                                                    → int { get; init; }
    .Failures                                          → IReadOnlyList<VectorItemFailure> { get; init; }
    .HasFailures                                                                       → bool { get; }

IVectorCollection<TRecord>   where TRecord : class, IVectorRecord
    .CollectionName                                                                 → string { get; }

    — write —
    .UpsertAsync(TRecord record, TenantScope tenantScope, ct)         → Task<Result<VectorWriteReceipt>>
    .UpsertManyAsync(IReadOnlyCollection<TRecord> records,
                     TenantScope tenantScope, ct)                     → Task<Result<VectorBulkReceipt>>
    .DeleteAsync(string id, TenantScope tenantScope, ct)              → Task<Result<VectorWriteReceipt>>
    .DeleteManyAsync(IReadOnlyCollection<string> ids,
                     TenantScope tenantScope, ct)                     → Task<Result<VectorBulkReceipt>>
    .DeleteByFilterAsync(VectorFilter filter, TenantScope tenantScope, ct)
                                                                       → Task<Result<VectorWriteReceipt>>
    .WaitUntilQueryableAsync(VectorWriteReceipt receipt, TimeSpan timeout, ct) → Task<Result>

    NOTE (NO WriteConsistency / CONSISTENCY-LEVEL PARAMETER ON ANY WRITE — the single most important
          "one more knob" the design explicitly REJECTED): Qdrant's write model (a `wait` boolean —
          block until searchable, or return immediately) and Milvus's write model (insert returns
          immediately; visibility is governed by the CONSISTENCY LEVEL OF A SUBSEQUENT QUERY, e.g.
          Strong/Bounded/Eventually — not a property of the write call at all) are not the same knob
          wearing different clothes. A shared WriteConsistency enum on the write signature would be
          honestly implementable on Qdrant and would NOT map onto anything Milvus's write call actually
          controls — exactly the "consistency level" example this domain's own seam-rule guidance names
          as a lie about the other engine. Instead: every write returns immediately with a receipt, and
          WaitUntilQueryableAsync is a SEPARATE, explicit, opt-in deferred barrier (mirroring
          09.Search's WaitUntilSearchableAsync) — Qdrant polls the point via its own client until
          visible; Milvus re-issues a bounded read using the receipt's ProviderToken as a
          guarantee_timestamp until it succeeds or the timeout elapses. Both are honest
          implementations of the SAME neutral primitive without inventing a fake shared write-time knob.

    NOTE (UPSERT ONLY): UpsertAsync is keyed on IVectorRecord.Id — no Create-vs-Update split, mirroring
          09.Search's IndexAsync reasoning (an upstream sync pipeline's at-least-once delivery makes
          the distinction meaningless on both engines).

    NOTE (NO OPTIMISTIC CONCURRENCY): neither Qdrant nor Milvus offers a comparable primitive to EF
          Core's rowversion or ElasticSearch's if_seq_no. A sync pipeline feeding these methods MUST
          guarantee ordering upstream by partitioning the change stream on Id — documented, not
          enforced, mirroring 09.Search's identical decision on IndexAsync.

    NOTE (BULK PARTIAL FAILURE IS NOT COLLAPSED): UpsertManyAsync/DeleteManyAsync return
          Result<VectorBulkReceipt>, and the Result is SUCCESS even when Failures is non-empty — both
          engines' batch APIs report per-item failures within an otherwise-successful batch call.
          Result.Failure is reserved for "the request itself did not execute" (e.g. a
          model-identity/dimension mismatch caught before any I/O).

    NOTE (DeleteByFilterAsync TAKES TenantScope AS A MANDATORY SEPARATE PARAMETER): never part of the
          VectorFilter tree — a dropped tenant clause on a bulk delete is cross-tenant data destruction.

    NOTE (WaitUntilQueryableAsync MUST NOT BE USED FOR READ-YOUR-WRITES ON A REQUEST PATH): it is the
          deferred barrier for background/bulk-import callers wanting one barrier after N writes
          instead of N barriers. Returns IntelligenceErrors.WriteTimeout on expiry — the write may
          still land; the error says so explicitly so callers do not retry blindly assuming it did not.

    NOTE (MODEL-IDENTITY/DIMENSION/METRIC VALIDATION IS THE FIRST THING EVERY WRITE DOES): before any
          I/O, the adapter checks record.ModelId == definition.EmbeddingModelId and
          record.Vector.Length == definition.Dimension, returning IntelligenceErrors.
          EmbeddingModelMismatch / .DimensionMismatch on failure. DistanceMetric has no per-record
          representation to validate — it is a property of the collection alone.
```

### Vector collection — read surface (`Abstractions/`)

```text
VectorHit<TRecord>   (sealed record)   where TRecord : class, IVectorRecord
    .Record                                                                    → TRecord { get; init; }
    .Score                                                                     → float { get; init; }
    .Rank                                                                        → int { get; init; }

    NOTE (Score EXISTS — THE ONE DELIBERATE DEVIATION FROM 09.Search's Score BAN, DECIDED, NOT
          DEFAULTED): 09.Search banned a raw relevance score outright because BM25 and Meilisearch's
          ranking-rule bucket-sort share no scale. This domain's own phase brief explicitly frames
          similarity score as "often genuinely load-bearing for a caller" (e.g. "only surface chunks
          above cosine 0.75") in a way full-text relevance rarely is, so an undocumented omission would
          just push callers onto a provider-exclusive escape hatch for a mainstream RAG use case. Score
          is kept, with the loudest possible XML doc warning on the member itself: THE SCALE IS
          PROVIDER- AND METRIC-SPECIFIC. Cosine similarity is bounded [-1, 1] (or [0, 1] depending on
          normalisation); dot-product is UNBOUNDED and depends on vector magnitude; Euclidean DISTANCE
          is smaller-is-better, the OPPOSITE direction of the other two. A threshold tuned against one
          provider/metric pairing is silently meaningless against another — Score MUST NOT be
          persisted, compared across a provider swap, or compared across a DistanceMetric change,
          without re-deriving the threshold empirically against the new pairing. Rank (0-based ordinal
          within the result page) is the portable substitute for callers who want ordering without
          touching Score at all, mirroring 09.Search exactly.

VectorQuery   (sealed record — public init members, no builder; matches SearchRequest's plain-record
              shape rather than adding fluent-builder ceremony this domain's smaller surface does not
              need)
    .Vector                                                          → ReadOnlyMemory<float> { get; init; }
    .ModelId                                                                     → string { get; init; }
    .Filter                                                              → VectorFilter? { get; init; }   (default null)
    .Limit                                                                          → int { get; init; }   (default IntelligenceWellKnown.DefaultQueryLimit)
    .MinScore                                                                    → float? { get; init; }   (default null)
    .ReturnMetadata                                                                → bool { get; init; }   (default true)
    .ReturnVector                                                                  → bool { get; init; }   (default false)

    NOTE (Vector AND ModelId ARE BOTH REQUIRED, NON-DEFAULTED): unlike SearchRequest.FreeText (which
          is optional — a filter-only search is legal), a similarity query with no query vector is not
          a coherent operation in this domain at all, so both are required init members with no
          sentinel-default meaning "no vector". ModelId is validated against
          VectorCollectionDefinition.EmbeddingModelId before any I/O, identically to the write path.

    NOTE (MinScore IS DOCUMENTED AS PROVIDER-AND-METRIC-SPECIFIC, SAME WARNING AS VectorHit.Score):
          it is applied server-side where the engine supports a score threshold, and validated for
          directional sanity per DistanceMetric where feasible (Euclidean: smaller-is-better, so a
          MinScore filters differently than for Cosine) — the concrete enforcement is a Core-phase,
          per-provider decision, not solved at the neutral layer beyond documenting the hazard.

    NOTE (ReturnVector DEFAULTS FALSE): vectors are large (a 1536-dim float32 vector is 6 KB) and most
          callers only need Metadata + Score. Opt-in keeps the default response cheap.

    NOTE (NO FREE TEXT MEMBER — THERE IS NO ANALOGUE HERE): unlike SearchRequest.FreeText, there is no
          "text" concept in a vector query distinct from the Vector itself — whatever text the caller
          wants matched IS what produced Vector via IEmbeddingGenerator upstream of this call.

VectorQueryResults<TRecord>   (sealed record)   where TRecord : class, IVectorRecord
    .Hits                                                → IReadOnlyList<VectorHit<TRecord>> { get; init; }
    .Duration                                                                  → TimeSpan { get; init; }
    .Empty                                                       → static VectorQueryResults<TRecord> { get; }

IVectorCollection<TRecord>   (read members, continued from the write surface above)
    .QueryAsync(VectorQuery query, TenantScope tenantScope, ct)
                                                          → Task<Result<VectorQueryResults<TRecord>>>
    .GetAsync(string id, TenantScope tenantScope, ct)                     → Task<Result<TRecord>>
    .CountAsync(VectorFilter? filter, TenantScope tenantScope, ct)        → Task<Result<long>>

    — corpus walk —
    .ScrollAsync(VectorFilter? filter, TenantScope tenantScope, int batchSize,
                 [EnumeratorCancellation] ct)                              → IAsyncEnumerable<TRecord>

    NOTE (GetAsync IS TENANT-CHECKED): implemented as a filtered point-lookup, not a raw get, whenever
          the collection definition declares a TenantField — a caller must not read another tenant's
          record by guessing an id. A miss returns IntelligenceErrors.RecordNotFound — never null,
          never a thrown exception.

    NOTE (CountAsync IS EXACT ON BOTH ENGINES): Qdrant's count endpoint and Milvus's query with
          COUNT(*) both return exact counts — unlike 09.Search there is no engine-side estimate to
          reconcile, so there is no accuracy qualifier on this member.

    NOTE (ScrollAsync IS NOT Result-WRAPPED — Domain Invariant #7): a transport failure mid-stream
          surfaces as IntelligenceStreamException from MoveNextAsync. It is a RECORD WALK (Qdrant
          scroll API / Milvus query iterator), not a ranked similarity search — it takes a VectorFilter
          but no Vector, and exists for reindex/export/re-embed-source enumeration, exactly mirroring
          09.Search's EnumerateAsync. Ordering is UNSPECIFIED and must not be relied upon.
```

### Vector collection provisioning (`Abstractions/`)

```text
VectorCollectionCutoverRequest   (sealed record)
    .StagingCollectionName                                                      → string { get; init; }
    .LiveCollectionName                                                         → string { get; init; }
    .DeleteStagingAfterCutover                                                  → bool { get; init; }   (default true)

VectorCollectionHealth   (sealed record)
    .Reachable                                                                     → bool { get; init; }
    .CollectionAddressable                                                         → bool { get; init; }
    .Queryable                                                                     → bool { get; init; }
    .VectorCount                                                                    → long { get; init; }
    .PendingWriteCount                                                            → long? { get; init; }
    .EngineVersion                                                               → string { get; init; }
    .SchemaFingerprint                                                          → string? { get; init; }
    .Latency                                                                   → TimeSpan { get; init; }

    NOTE (CollectionAddressable IS SEPARATE FROM Reachable): a reachable cluster with a missing or
          mis-aliased collection, or a mis-scoped API key for THIS collection specifically, passes a
          cluster-wide health check and returns 100% production failures — the identical
          SearchIndexHealth precedent from 09.Search, applied here.

    NOTE (PendingWriteCount IS NULLABLE, PERMANENTLY): Qdrant's optimizer-status pending-operations
          signal and Milvus's segment-flush backlog are not the same shape, and one engine may expose
          nothing comparable at all depending on version. Nullability models the capability gap
          honestly rather than reporting a fabricated 0. A deep backlog is never a readiness
          FAILURE — it means results may be stale, not unavailable.

    NOTE (VectorCollectionHealth IS WHAT THE READINESS PROBE MEASURES): no public contract returns it;
          the provider's internal measurement feeds VectorCollectionReadinessProbe below.

VectorCollectionReadinessProbe   (sealed class : SharedKernel.Primitives.Health.IReadinessProbe)
    .ctor(string providerName, string collectionName,
          Func<string, CancellationToken, Task<Result<VectorCollectionHealth>>> probe)
    .Name                                            → string   ("vector-store-{provider}-{collection}")
    .ProbeNameFor(string providerName, string collectionName)                    → static string
    .ProbeAsync(ct)                                                            → Task<ReadinessReport>
    ProviderKey / CollectionKey / ReachableKey / CollectionAddressableKey / QueryableKey /
    VectorCountKey / PendingWriteCountKey / EngineVersionKey / SchemaFingerprintKey / ErrorCodeKey
                                                                    (const string ReadinessReport.Data keys)

    NOTE: each provider's builder registers ONE per registered collection (AddReadinessProbe), over
          its own internal measurement. Healthy = Reachable AND CollectionAddressable AND Queryable;
          a failed measurement is Unhealthy carrying ErrorCodeKey. The host maps every probe with
          services.AddHealthChecks().AddSharedKernelReadiness(); nothing in this domain references
          Microsoft.Extensions.Diagnostics.HealthChecks.

IVectorCollectionProvisioner   (non-generic — exactly one registration per provider)
    .EnsureCollectionAsync(VectorCollectionDefinition definition, ct)             → Task<Result>
    .CollectionExistsAsync(string collectionName, ct)                            → Task<Result<bool>>
    .DeleteCollectionAsync(string collectionName, ct)                            → Task<Result>
    .CutoverAsync(VectorCollectionCutoverRequest request, ct)                     → Task<Result>

    NOTE (EnsureCollectionAsync IS IDEMPOTENT AND ADDITIVE-ONLY): creates the collection if absent and
          applies the field declarations (payload/scalar indexes on Filterable fields). It NEVER
          rewrites an incompatible existing definition — a definition conflicting with the live
          collection returns IntelligenceErrors.CollectionDefinitionConflict; the remedy is staging →
          UpsertManyAsync → CutoverAsync, exactly mirroring 09.Search's EnsureIndexAsync. It also
          persists Fingerprint so the readiness probe can report drift — Qdrant via its own genuine
          collection-level metadata map (CORRECTED at Core-phase implementation time, 2026-07-24:
          Qdrant.Client 1.18.1's CreateCollectionAsync/UpdateCollectionAsync genuinely accept a
          Dictionary<string, Value> metadata parameter, confirmed via assembly reflection and a real
          server round-trip against Qdrant server v1.16.0+ — the Design-phase "reserved sentinel
          point" assumption below was never built and is superseded), Milvus via a native
          collection property (Milvus 2.4+ supports arbitrary collection properties directly) — each
          provider's concrete persistence mechanism is a Core-phase decision for P-280/P-281
          respectively, not solved at this neutral layer beyond requiring that BOTH detect drift.

    NOTE (CutoverAsync — BOTH ENGINES GENUINELY HAVE ALIASES, UNLIKE 09.Search's ASYMMETRIC CASE):
          Qdrant and Milvus both support native collection aliases (create/alter alias), so
          LiveCollectionName is an ALIAS on BOTH providers and the swap is atomic on both — a cleaner,
          more symmetric case than 09.Search's ES-alias-vs-Meilisearch-swap-endpoint asymmetry.
          DeleteStagingAfterCutover (default true) still exists because the staging collection's
          underlying data remains allocated until explicitly deleted on both engines.

    NOTE (WHAT THIS DOES NOT OWN — Domain Invariant #1): the re-embedding/rebuild itself. There is no
          IIndexRebuilder-shaped type driving a data source, because the source-entity → embedding
          mapping is business logic belonging to the owning service, and a rebuild source would force
          a 06.Persistence or 07.Messaging reference this layer may not take. The consumer sequences:
          EnsureCollectionAsync(staging) → UpsertManyAsync(from its own IAsyncEnumerable, fed through
          its own IEmbeddingGenerator calls) → CutoverAsync → optional DeleteCollectionAsync.

    NOTE (NO PROBE MEMBER — Domain Invariant #8): readiness is VectorCollectionReadinessProbe above.
          ICompletionProviderDescriptor likewise has no probe: an honest reachability check for a
          completion endpoint is a real, billed call, which Domain Invariant #5 forbids.
```

### Vector provider descriptor (`Abstractions/`)

```text
IVectorProviderDescriptor   (singleton, zero I/O)
    .ProviderName                                                                 → string { get; }
    .MaxBatchSize                                                                    → int { get; }
    .MaxVectorDimension                                                                → int { get; }
    .MaxFilterDepth                                                                     → int { get; }
    .RegisteredCollections                                                → IReadOnlyList<string> { get; }
    .Validate(string collectionName, VectorQuery query)                            → Result

    NOTE: ProviderName is IntelligenceWellKnown.QdrantProviderName, and is also the value of the
          "ai.provider" OTel tag.

    NOTE (NO CAPABILITY-FLAGS ENUM, DELIBERATELY — same reasoning as 09.Search's
          ISearchProviderDescriptor): an `if (caps.HasFlag(...))` branch at an application call site is
          a platform violation here specifically because degradation in similarity search is
          CONFIDENTLY WRONG rows, not merely fewer rows. The consumer-facing capability mechanism is
          the compile error a provider swap produces against provider-package-declared exclusive
          contracts, never a runtime flag.

    NOTE (Validate IS THE ZERO-I/O, ZERO-CONTAINER PRE-FLIGHT): checks MaxFilterDepth against the
          query's VectorFilter tree depth, MaxVectorDimension against query.Vector.Length, and
          structural request invariants — without touching the network, mirroring
          ISearchProviderDescriptor.Validate exactly. It does NOT check model-identity/dimension
          against a specific collection's definition (that check needs the definition, which the
          descriptor does not hold) — that remains IVectorCollection<TRecord>'s own responsibility at
          call time.
```

### Filter AST (`Models/`)

```text
VectorFilter   (abstract record, CLOSED hierarchy — private protected base ctor; the eight sealed
                subtypes are PUBLIC with INTERNAL constructors and public get-only properties)
    EqualFilter      { Field: string, Value: VectorValue }
    NotEqualFilter   { Field: string, Value: VectorValue }
    InFilter         { Field: string, Values: IReadOnlyList<VectorValue> }
    RangeFilter      { Field: string, From: VectorValue?, FromInclusive: bool,
                       To: VectorValue?, ToInclusive: bool }
    ExistsFilter     { Field: string }
    AndFilter        { Operands: IReadOnlyList<VectorFilter> }
    OrFilter         { Operands: IReadOnlyList<VectorFilter> }
    NotFilter        { Operand: VectorFilter }

    — the ONLY sanctioned construction path, static factories on the base —
    .Eq(string field, VectorValue value)                                          → VectorFilter
    .Ne(string field, VectorValue value)                                          → VectorFilter
    .In(string field, params VectorValue[] values)                                → VectorFilter
    .Between(string field, VectorValue? from, VectorValue? to,
             bool fromInclusive = true, bool toInclusive = true)                  → VectorFilter
    .Exists(string field)                                                        → VectorFilter
    .All(params VectorFilter[] operands)                                         → VectorFilter
    .Any(params VectorFilter[] operands)                                         → VectorFilter
    .Negate(VectorFilter operand)                                                → VectorFilter

    NOTE (DELIBERATE STRUCTURAL TWIN OF 09.Search's SearchFilter — SAME 8 NODES, SAME CLOSED-BY-
          CONSTRUCTION MECHANISM): private protected base + internal subtype constructors + public
          types make each provider's translation switch exhaustive and compiler-checked. HARD RULE:
          neither Qdrant's nor Milvus's translation switch may carry a discard (`_ =>`) arm.

    NOTE (BOTH ENGINES GENUINELY EXPRESS ALL EIGHT NODES, CONFIRMED AT DESIGN TIME BY CHECKING EACH
          ENGINE'S REAL FILTER GRAMMAR, NOT ASSUMED): Equal/In/Range/And/Or map directly onto both
          Qdrant's Match/MatchAny/Range + must/should conditions and Milvus's boolean-expression
          operators. NotEqual and Not are synthesised via must_not-wrapping on Qdrant (no native
          "not equal" match primitive) and via `!=`/`not` directly on Milvus — both are FAITHFUL
          translations, not degradations; synthesis via composition is normal compiler work, not the
          disqualifying pattern. Exists is `IsEmpty`-negation on Qdrant and `IS NOT NULL` on Milvus
          (which requires the field declared nullable=true at collection-creation time — the Milvus
          provider's EnsureCollectionAsync sets this automatically for every Filterable field, a
          Core-phase implementation detail, not a neutral-surface concern).

    CORRECTED (Tests-phase real-container proof, T-03, 2026-07-24): the Design-time note above
          originally read "Exists is IsNull-negation on Qdrant" — this was WRONG, verified against a
          real Qdrant server, not merely against the client SDK's type names. Qdrant's IsNullCondition
          matches only a payload key that is genuinely PRESENT with a JSON null value; it does NOT
          match a key that is entirely absent from the payload. A `must_not: [is_null(field)]`
          translation is therefore vacuously true for every record regardless of whether the field was
          ever set — a confirmed, shipped Core-phase defect (every record matched Exists("tags"),
          including ones that never declared the field). Qdrant's IsEmptyCondition matches a key that
          is absent OR an empty array OR (per Qdrant's own documented semantics) a JSON null value; its
          negation, `must_not: [is_empty(field)]`, is the correct "field genuinely present with a real
          value" translation and is what `QdrantFilterCompiler` now implements.

    NOTE (Between REJECTS String AND Boolean BOUNDS — CONSISTENCY CHOICE, RECORDED AS SUCH): Qdrant's
          Range condition is numeric/datetime only, with no lexicographic string-range primitive at
          all — a hard impossibility on that engine, identical to 09.Search's reasoning. Milvus's
          expression grammar CAN express a lexicographic VARCHAR range; it is deliberately left unused
          here for platform-wide consistency with the identical rule in 09.Search's SearchFilter,
          rather than because Milvus cannot do it. A developer who already knows one closed-AST domain
          in this platform should not be surprised by different Between semantics in the other. Only
          Int64, Double, and DateTimeOffset are legal range bounds; Between throws ArgumentException
          for a String or Boolean VectorValue — a programming error caught at first test run.

    NOTE (WHAT IS DELIBERATELY ABSENT, and why): no free-text/full-text node — there is no analogue in
          a vector-metadata filter; whatever "text search" means here happens through the embedding
          Vector itself, upstream of this AST entirely. No Fuzzy/TypoTolerance (not a metadata-filter
          concept on either engine). No Boost/FunctionScore (relevance here IS Score, already a
          documented hazard on VectorHit — a second, filter-level boost knob would compound it). No
          GeoRadius (deferred, same reasoning as 09.Search — real-container verification before
          guessing at semantics). No Prefix/Wildcard/Regex. No nested/object-array filter — same
          correctness hazard 09.Search identified (element-correlation loss on flattening engines);
          the mandated portable technique is identical: flatten to a precomputed composite Filterable
          field at write time and filter it with In(...).
```

### Tenant scope (`SharedKernel.Execution.Tenancy` — not declared here)

```text
TenantScope   (readonly record struct, SharedKernel.Execution — the platform's one tenant scope)
    .Tenant                                                                     → TenantId? { get; }
    .IsGlobal                                                                       → bool { get; }
    .Global                                                           → static TenantScope { get; }
    .For(TenantId tenant) / .FromNullable(TenantId? tenant)                         → TenantScope

    NOTE: the same type 09.Search, 17.Workflows and 19.Scheduling use. Adapters write and filter the
          tenant field as the TenantId's "D" string, so a tenant field holds GUID strings.
          Global (= default) is the explicit single-tenant/global-collection sentinel;
          For(default(TenantId)) throws ArgumentException — a programming error.

    NOTE (MANDATORY, NON-NULLABLE, NON-DEFAULTED — Domain Invariant #3): a separate method parameter
          on every IVectorCollection<TRecord> read and every filtered/bulk write, never a member of
          VectorQuery. Adapters inject it as the OUTERMOST AND clause after translating the caller's
          VectorFilter.

    NOTE (FAIL CLOSED, DRIVEN BY VectorCollectionDefinition.TenantField): if the registered definition
          declares a TenantField and the caller passes TenantScope.Global, the provider returns
          IntelligenceErrors.TenantScopeMissing and performs NO I/O — the guard arms itself
          automatically the moment a collection is genuinely multi-tenant, exactly mirroring
          09.Search's identical decision.
```

### Collection definition and fingerprint (`Models/`) — the surface to guard hardest

```text
VectorDistanceMetric   (enum)
    Cosine = 0, DotProduct = 1, Euclidean = 2

    NOTE: all three are genuinely, faithfully supported by BOTH Qdrant (Cosine/Dot/Euclid) and Milvus
          (COSINE/IP/L2) — confirmed at Design time, not assumed. No fourth metric is offered because a
          fourth candidate has not been confirmed present on both engines; adding one later requires
          the same two-engine confirmation this enum received.

VectorFieldKind   (enum)
    String = 0, Int64 = 1, Double = 2, Boolean = 3, DateTimeOffset = 4

    NOTE: intentionally identical in shape to VectorValueKind — a metadata field's declared Kind is
          exactly which VectorValue accessor a filter against it must use.

VectorFieldDefinition   (sealed record)
    .Name                                                                          → string { get; init; }
    .Kind                                                                → VectorFieldKind { get; init; }
    .Filterable                                                                    → bool { get; init; }   (default false)

    NOTE (ONE BOOLEAN, DELIBERATELY, NOT FOUR LIKE SearchFieldDefinition): there is no Searchable
          (no full-text concept on vector metadata), no Sortable (similarity query results are always
          ordered by Score, and ScrollAsync's order is unspecified by design), no Facetable (faceting
          is a full-text-search concept absent from both vector engines' actual feature sets). A field
          must be Filterable=true to appear in a VectorFilter against it — this is what drives payload-
          index creation on Qdrant and scalar-index + nullable=true on Milvus at EnsureCollectionAsync
          time. THIS TYPE, ALONGSIDE VectorCollectionDefinition BELOW, IS THE SURFACE TO GUARD HARDEST
          IN REVIEW: a request for a fifth boolean (e.g. "sortable" for some future re-ranking feature)
          is very likely a claim about one engine that does not hold for the other and must clear the
          seam rule explicitly before it lands.

VectorCollectionDefinition   (sealed record)
    .Name                                                                          → string { get; init; }
    .EmbeddingModelId                                                             → string { get; init; }
    .Dimension                                                                        → int { get; init; }
    .DistanceMetric                                                    → VectorDistanceMetric { get; init; }
    .TenantField                                                                  → string? { get; init; }   (default null)
    .Fields                                             → IReadOnlyList<VectorFieldDefinition> { get; init; }
    .Create(string name, string embeddingModelId, int dimension,
            VectorDistanceMetric metric, IReadOnlyList<VectorFieldDefinition> fields)
                                                                        → Result<VectorCollectionDefinition>
    .Fingerprint                                                                     → string { get; }

    NOTE (THIS IS THE DOMAIN'S SHARPEST-EDGE TYPE — Domain Invariants #1 and #2 in one place):
          EmbeddingModelId + Dimension + DistanceMetric together are what every write and query
          validates against before any I/O. TenantField placement here (not in provider options)
          mirrors 09.Search's SearchIndexDefinition.TenantField exactly, for the identical reason — a
          service may legitimately have one tenanted collection and one global one, and per-collection
          placement is what lets the fail-closed guard arm itself exactly where it should.

    NOTE (Fingerprint — ALGORITHM PINNED AT DESIGN TIME): SHA-256 over a canonical UTF-8 string,
          rendered as lowercase hex of the 32 bytes, one line per component with '\n' separators, no
          default-value elision:
              Name
              EmbeddingModelId
              Dimension               (invariant culture)
              (int)DistanceMetric     (invariant culture)
              TenantField ?? ""
              then, for each field sorted by Name using StringComparer.Ordinal:
              Name|{(int)Kind}|{Filterable:0|1}
          Ordinal sorting makes the value independent of declaration order; explicit ints make it
          independent of enum member renames — identical technique to SearchIndexDefinition.Fingerprint
          (09.Search), reused deliberately rather than reinvented. Uses System.Security.Cryptography —
          in-box on net10.0, so .Abstractions keeps its zero-PackageReference guarantee.

VectorCollectionDefinitionBuilder   (sealed class)
    .EmbeddingModel(string modelId, int dimension)                     → VectorCollectionDefinitionBuilder
    .DistanceMetric(VectorDistanceMetric metric)                       → VectorCollectionDefinitionBuilder
    .TenantField(string field)                                        → VectorCollectionDefinitionBuilder
    .Field(string name, VectorFieldKind kind, bool filterable = false) → VectorCollectionDefinitionBuilder
    .Build()                                                                    → Result<VectorCollectionDefinition>
```

### LLM orchestration (`Abstractions/`)

```text
ChatRole   (enum)
    System = 0, User = 1, Assistant = 2, Tool = 3

ChatMessage   (sealed record)
    .Role                                                                     → ChatRole { get; init; }
    .Content                                                                    → string { get; init; }
    .Name                                                                     → string? { get; init; }   (default null — tool/function name attribution)

ToolDefinition   (sealed record)
    .Name                                                                       → string { get; init; }
    .Description                                                                → string { get; init; }
    .ParametersJsonSchema                                                       → string { get; init; }

    NOTE (ParametersJsonSchema IS A PLAIN STRING, NOT A REFLECTED Type): the caller supplies a JSON
          Schema document as text. This keeps .Abstractions reflection-free and AOT-clean — the
          reflection-heavy work of turning a C# delegate/Type into a schema (which
          Microsoft.SemanticKernel's [KernelFunction] discovery does internally) stays entirely inside
          SharedKernel.AI.SemanticKernel, never touching the neutral contract.

ToolCallRequest   (sealed record)
    .CallId                                                                       → string { get; init; }
    .Name                                                                         → string { get; init; }
    .ArgumentsJson                                                                → string { get; init; }

ToolCallResult   (sealed record)
    .CallId                                                                       → string { get; init; }
    .ResultJson                                                                   → string { get; init; }
    .ToMessage()                                                                → ChatMessage   (Role = Tool, Name = CallId, Content = ResultJson — convenience only)

CompletionFinishReason   (enum)
    Stop = 0, MaxTokensReached = 1, ToolCallsRequested = 2, ContentFiltered = 3

CompletionRequest   (sealed record)
    .Messages                                                    → IReadOnlyList<ChatMessage> { get; init; }
    .ModelId                                                                     → string? { get; init; }   (default null → provider default)
    .Temperature                                                                → float? { get; init; }   (default null → provider default)
    .MaxOutputTokens                                                              → int? { get; init; }   (default null → provider default)
    .Tools                                                     → IReadOnlyList<ToolDefinition> { get; init; }   (default [])
    .StopSequences                                                    → IReadOnlyList<string> { get; init; }   (default [])

CompletionResult   (sealed record)
    .Message                                                                → ChatMessage { get; init; }
    .ModelId                                                                       → string { get; init; }
    .TokenUsage                                                              → TokenUsage { get; init; }
    .FinishReason                                                    → CompletionFinishReason { get; init; }
    .ToolCalls                                              → IReadOnlyList<ToolCallRequest> { get; init; }   (default [])

CompletionChunk   (sealed record)
    .DeltaContent                                                                → string { get; init; }
    .FinishReason                                                     → CompletionFinishReason? { get; init; }   (null until the final chunk)
    .TokenUsage                                                                → TokenUsage? { get; init; }   (null until the final chunk)

ISemanticKernel
    .CompleteAsync(CompletionRequest request, ct)                       → Task<Result<CompletionResult>>
    .CompleteStreamingAsync(CompletionRequest request,
                            [EnumeratorCancellation] ct)                → IAsyncEnumerable<CompletionChunk>

    NOTE (TOOL EXECUTION IS NEVER PERFORMED BY THIS CONTRACT — THE STATELESSNESS BOUNDARY THE PHASE
          BRIEF REQUIRES): there is no InvokeToolAsync member and no agent/planner loop anywhere on
          ISemanticKernel. When FinishReason == ToolCallsRequested, the CALLER executes each
          ToolCallRequest against its own business logic, builds a ToolCallResult, appends
          .ToMessage() to the growing Messages list, and issues a follow-up CompleteAsync call. Tool
          execution is arbitrary consumer-owned business logic — invoking it from this layer would
          require exactly the kind of reflection-driven dynamic dispatch (or a domain-logic reference)
          this package must never take. This is the direct implementation of the phase brief's "bounded
          so it does not leak stateful multi-step orchestration into what must remain a stateless
          capability package" requirement, and mirrors 09.Search's "no IIndexRebuilder" reasoning: the
          contract makes the multi-turn NEED visible (ToolCallsRequested + ToolCalls) without owning
          the loop.

    NOTE (CompleteStreamingAsync IS NOT Result-WRAPPED — Domain Invariant #7): mid-stream faults
          surface as IntelligenceStreamException from MoveNextAsync. TokenUsage is null on every chunk
          except the final one, since most providers report usage only once the stream completes — a
          consumer wanting live-accumulated usage sums PromptTokens/CompletionTokens itself if the
          provider streams partial counts (documented per-provider, not guaranteed neutrally).

    NOTE (NO RETRY MEMBER ANYWHERE ON THIS INTERFACE — Domain Invariant #5): a retry re-bills and
          re-rolls a non-deterministic output. If a provider package offers retry at all, it is an
          explicit, bounded, opt-in DI builder call (e.g. SharedKernel.AI.SemanticKernel's
          `.WithBoundedRetry(...)`), never automatic and never reachable through this interface itself.

    NOTE (NO CACHING MEMBER ANYWHERE ON THIS INTERFACE — Domain Invariant #4): CompleteAsync always
          dispatches a fresh call. A caller wanting a cached completion built a cache themselves at
          their own layer with their own explicit opt-in — this package must never silently serve a
          stale completion the caller did not ask for, and 10.Intelligence may not reference
          02.Caching in any case.

ICompletionProviderDescriptor   (singleton, zero I/O — the orchestration-side sibling of
                                 IVectorProviderDescriptor; kept as a SEPARATE interface because a
                                 vector engine's "max batch size, max dimension, filter depth" and an
                                 LLM's "context window, max output tokens" share no members that both
                                 would honestly implement — forcing them onto one type would itself be
                                 a seam-rule violation)
    .ProviderName                                                                  → string { get; }
    .ContextWindowTokens                                                            → int { get; }
    .MaxOutputTokens                                                                 → int { get; }
    .ValidateContextWindow(int estimatedTokens)                                    → Result

    NOTE (ValidateContextWindow IS THE PRE-DISPATCH GUARD — Domain Invariant #5): a zero-I/O check a
          caller can invoke ahead of a CompleteAsync call, and which the adapter also invokes
          internally before dispatch wherever it can cheaply estimate token count. Returns
          IntelligenceErrors.ContextWindowExceeded(limit, actual) — never a thrown exception, never a
          silent truncation of the caller's messages.

    NOTE (NO SupportsStreaming OR SIMILAR BOOLEAN): a capability boolean whose only purpose is an
          `if (descriptor.X)` branch at a call site is the same runtime-capability-flag violation the
          vector side rejects. CompleteStreamingAsync exists on ISemanticKernel only because it is
          genuinely, faithfully implementable by every provider this domain ships against.
```

### Well-known constants (`Constants/`)

```text
IntelligenceWellKnown   (public static class — SK0022 named-constant holder)
    DefaultQueryLimit           const int    = 10
    MaxQueryLimit                const int    = 1000
    ActivitySourceName            const string = "SharedKernel.AI"
    MeterName                       const string = "SharedKernel.AI"
    QdrantProviderName                 const string = "qdrant"
    SemanticKernelProviderName               const string = "semantickernel"
    ProviderTagName                             const string = "ai.provider"
    CollectionTagName                              const string = "ai.collection"
    ModelTagName                                      const string = "ai.model"

    NOTE: mirrors SearchWellKnown's placement rationale exactly — lives in .Abstractions specifically
          so every sibling provider reads the BYTE-IDENTICAL ActivitySource/Meter name and OTel tag
          keys, which is the entire reason 13.ServiceDefaults can wire one string name and cover every
          provider with no ProjectReference to 10.Intelligence. The ActivitySource/Meter INSTANCES are
          created per-provider; only the names live here.

    NOTE: a future second vector-database provider declares its own provider-name constant; the
          provider name is also the middle segment of its readiness-probe names.
```

### Errors (`Errors/`)

```text
IntelligenceErrors   (public static class — canonical Error factory; provider implementations return
                      these and never construct ad-hoc Error values inline)

    — Error.NotFound —
    .CollectionNotFound(collectionName)                            "intelligence.collection_not_found"
    .RecordNotFound(collectionName, recordId)                      "intelligence.record_not_found"
    .ModelNotFound(modelId)                                        "intelligence.model_not_found"

    — Error.Validation —
    .InvalidQuery(reason)                                          "intelligence.invalid_query"
    .InvalidFilter(reason)                                         "intelligence.invalid_filter"
    .InvalidCollectionDefinition(reason)                    "intelligence.invalid_collection_definition"
    .InvalidRecordId(recordId)                                     "intelligence.invalid_record_id"
    .FieldNotFilterable(collectionName, field)                     "intelligence.field_not_filterable"
    .EmbeddingModelMismatch(collectionName, expected, actual)  "intelligence.embedding_model_mismatch"
    .DimensionMismatch(collectionName, expected, actual)           "intelligence.dimension_mismatch"
    .DistanceMetricMismatch(collectionName, expected, actual) "intelligence.distance_metric_mismatch"
    .BatchSizeExceeded(requested, ceiling, providerName)           "intelligence.batch_size_exceeded"
    .FilterDepthExceeded(requested, ceiling)                       "intelligence.filter_depth_exceeded"
    .ContextWindowExceeded(limit, actual)                        "intelligence.context_window_exceeded"
    .UnsupportedCapability(capability, providerName)            "intelligence.unsupported_capability"

    — Error.Conflict —
    .CollectionAlreadyExists(collectionName)                 "intelligence.collection_already_exists"
    .CollectionDefinitionConflict(collectionName, field) "intelligence.collection_definition_conflict"
    .CutoverFailed(stagingCollectionName, liveCollectionName, reason) "intelligence.cutover_failed"
    .SchemaFingerprintMismatch(collectionName, expected, actual)
                                                          "intelligence.schema_fingerprint_mismatch"

    — Error.Unauthorized —
    .Unauthorized(collectionName, operation)                       "intelligence.unauthorized"
    .TenantScopeMissing(collectionName)                       "intelligence.tenant_scope_missing"

    — Error.Unexpected —
    .Unreachable(providerName, endpoint)                           "intelligence.unreachable"
    .Timeout(operation, elapsed)                                   "intelligence.timeout"
    .WriteRejected(collectionName, reason)                         "intelligence.write_rejected"
    .WriteTimeout(collectionName, elapsed)                         "intelligence.write_timeout"
    .BulkPartiallyFailed(failedCount, totalCount)              "intelligence.bulk_partially_failed"
    .ProbeFailed(collectionName, reason)                           "intelligence.probe_failed"
    .EngineVersionUnsupported(actual, supportedRange)     "intelligence.engine_version_unsupported"
    .EngineFault(providerName, operation, detail)                  "intelligence.engine_fault"
    .RateLimited(providerName, retryAfter)                         "intelligence.rate_limited"
    .CompletionFailed(providerName, reason)                        "intelligence.completion_failed"

    NOTE (THE Error FACTORIES): SharedKernel.Primitives' Error exposes Unexpected, Validation,
          NotFound, Conflict, Unauthorized, Forbidden, BusinessRule, Unavailable and Timeout — each
          (string code, string message) — plus the Error.None sentinel. There is NO Error.Failure. This
          domain uses Unexpected, Validation, NotFound, Conflict and Unauthorized, never Forbidden or
          BusinessRule; its outage and timeout codes are still Unexpected (a P-562 follow-up would move
          them to Unavailable/Timeout). Every code literal is a private const string on the holder
          class — never retyped at a call site (SK0022).

    NOTE (Error.BusinessRule IS USED ZERO TIMES): per ErrorType's own XML doc it maps to HTTP 422 and
          denotes a DOMAIN-RULE violation; nothing in a capability package is a domain rule.
          EmbeddingModelMismatch/DimensionMismatch/DistanceMetricMismatch/ContextWindowExceeded are
          Validation — the failing input is the caller's own record/query shape. TenantScopeMissing is
          Unauthorized — an isolation failure, and 401/403 is the honest boundary status.

    NOTE (Error.None IS NEVER RETURNED from any method in this domain).

    NOTE (RateLimited AND CompletionFailed ARE ORCHESTRATION-SIDE, EngineFault IS THE SHARED LAST-
          RESORT MAPPING): a rise in EngineFault is the signal a translator has drifted from a
          provider's current API surface, mirroring 09.Search's identical NOTE.
```

### Streaming exception (`Exceptions/`)

```text
IntelligenceStreamException   (sealed exception, derives directly from System.Exception)
    .Error                                                                       → Error { get; }

    NOTE: thrown from IVectorCollection<TRecord>.ScrollAsync and ISemanticKernel.CompleteStreamingAsync
          — the only two non-Result surfaces in the domain. Constructed only from a
          SharedKernel.Primitives Error (plus an optional inner Exception overload), never from a bare
          string (SK0003/SK0005). Derives directly from System.Exception for the identical reason
          SearchStreamException does — SharedKernel.Primitives ships no exception base at all, and the
          only Error-carrying exception hierarchy in the platform lives in SharedKernel.Core, a package
          this domain's reference set (SharedKernel.Primitives + SharedKernel.Execution) does not
          include.
```

---

## Implementation Rules

### The seam rule (the whole design in one line)

**`SharedKernel.AI.Abstractions` contains no type that a candidate provider cannot implement completely and correctly.** If implementing a member would require one adapter to throw, degrade, approximate, or no-op, that member does not belong in `.Abstractions` — it becomes a provider-package-declared exclusive contract, or it is declined. `VectorCollectionDefinition` / `VectorFieldDefinition` are the surfaces to guard hardest: every future "just one more knob" request (index type, quantization profile, **consistency level** — already rejected once, see the write-surface NOTE above — partition strategy) is a claim about one engine that is usually a lie about the other.

### Hard violations (never do these)

- `SharedKernel.AI.Abstractions` taking a `PackageReference` that has not been explicitly adjudicated and recorded in this file. The default is **zero** — `SharedKernel.Primitives` and `SharedKernel.Execution` as `ProjectReference`s only (SKTIER003 fails the build on any other third-party package in the Abstractions tier). `Microsoft.Extensions.AI.Abstractions` was explicitly adjudicated and **declined** — see Packages. (`System.Text.Json`, `System.Security.Cryptography`, and `System.Numerics.Tensors` are in-box on `net10.0` and add no dependency.)
- Sibling provider packages (`SharedKernel.AI.Qdrant`, `.SemanticKernel`, and any future sibling — e.g. a second vector-database provider) referencing each other, in either direction, at project or type level.
- Any provider package exposing a type from another provider's SDK, or from a sibling provider's own SDK, on its public surface.
- Referencing `SharedKernel.Domain`, the application pipeline, persistence, messaging, search, security or caching packages from any `10.Intelligence` package — `.Abstractions` stays on Foundation packages; the providers add only `SharedKernel.Configuration` and their vendor SDK.
- Declaring a provider-exclusive contract in `.Abstractions` — provider-package placement is what turns a swap into a compile error rather than a startup resolution error.
- Injecting a raw model SDK or vector-DB client type (`QdrantClient`, `Kernel`, an `OpenAIClient`, …) into application code, or exposing one from an abstraction member. Raw-client access, if offered at all, follows the `09.Search` three-gate pattern: opt-in builder call, startup `Warning`, and a governance architecture test — and its XML doc must state **in capitals** that the hatch bypasses tenant scoping.
- **Writing or querying a vector whose model identity, dimension, or distance metric does not match the collection's declaration**, or accepting the write/query and letting the engine sort it out. Reject before any I/O.
- Making `TenantScope` optional, nullable, defaulted, or a member of `VectorQuery`/any request object; or routing a tenant predicate through the caller-supplied `VectorFilter`.
- **Silently degrading, dropping, coercing, or post-filtering in memory** any clause the engine cannot express. Every rejection is a `Result` failure returned before any I/O.
- A runtime capability-flag check (`if (caps.HasFlag(...))`) at an application call site as the mechanism for provider differences.
- Adding a `WriteConsistency`/consistency-level parameter to any write member, or any other collection-definition knob that is honest on one engine and not the other, without first proving both engines genuinely support it (see the write-surface and `VectorFieldDefinition` NOTEs).
- Exposing `VectorHit.Score` / `VectorQuery.MinScore` without the provider-and-metric-specific documentation already locked on those members.
- Logging prompt text, completion text, retrieved `Metadata` values, raw vectors, or any credential.
- Adding a `Retry`-shaped member to `.Abstractions`, or silently retrying a completion in any provider by default. Retries re-bill and re-roll a non-deterministic output; they are explicit, bounded, opt-in, and never applied to a non-idempotent call by default.
- Adding a caching member or silent cache-serve path to `ISemanticKernel` or any provider's completion path. `10.Intelligence` may not reference `02.Caching` in any case.
- Adding an `InvokeToolAsync` or agent/planner-loop member to `ISemanticKernel` — tool execution is always consumer-owned.
- Wrapping a streaming member (`ScrollAsync`, `CompleteStreamingAsync`) in `Result`.
- Implementing `IHealthCheck`, or referencing `Microsoft.Extensions.Diagnostics.HealthChecks`, anywhere in `10.Intelligence`.
- Constructing an ad-hoc `Error` inline instead of routing through `IntelligenceErrors`. Naming a non-existent factory (`Error.Failure`) or returning `Error.None` are both violations; `Error.Forbidden` and `Error.BusinessRule` are not used anywhere in this domain.
- Bare string literals for config section paths (`SK0022` — the section name is declared on the options type), model identifiers, collection/field names, or OTel tag keys.
- Injecting raw `HttpClient` or calling `new HttpClient()` (`P-159`/`SK0013`).
- `Activator.CreateInstance`, `Assembly.Load`, `Type.GetProperty`/`GetMethod`, `MakeGenericMethod`/`MakeGenericType`, or `dynamic` in this domain's own code. Note `MakeGenericType` is **invisible to `SK0012`**, which matches `MakeGenericMethod` only — that gap is held by review, not by the analyzer. (This does not restrict `Microsoft.SemanticKernel`'s own internal reflection use, which is isolated inside `SharedKernel.AI.SemanticKernel` and never surfaced.)
- Any static mutable state.
- Registering two providers against the same collection/record type (`TRecord`) — the second unkeyed registration silently wins.
- Adding `<IsAotCompatible>true</IsAotCompatible>` to any `10.Intelligence` `.csproj`.

### Cross-domain touch points

Owned by other domains; change them there, never from here.

- **`13.ServiceDefaults`** — `WithIntelligenceTelemetry()` wires `IntelligenceWellKnown`'s source/meter names by string, and `AddSharedKernelReadiness()` maps every `vector-store-*` probe. There is no intelligence-specific ServiceDefaults package.
- **`16.Testing`** — `SharedKernel.AI.Testing` (in-memory doubles for every neutral contract) and `QdrantContainerFixture` in the non-packable `SharedKernel.Testing.Internal`. A new neutral member needs a matching change to the double.
- **`00.Governance`** — the tier check (`<SharedKernelTier>`, SKTIER001–006) replaces the old layering rules; topology rules lock sibling independence and the raw-client gates. Verify the highest allocated `SK0xxx` ID before claiming one.

---

## DI Registration

`SharedKernel.AI.Abstractions` ships **no DI extensions** — it is a pure abstraction library. All registration lives in the provider packages, behind an `AddSharedKernel{Provider}...(IServiceCollection, IConfiguration)` entry point returning a fluent builder terminated by `.Build()`, matching `08.Storage` and `09.Search`.

- **Vector provider** (`AddSharedKernelQdrant()`): `.AddCollection<TRecord>(collectionName, configure)` registers one `IVectorCollection<TRecord>` per `TRecord`; `IVectorCollectionProvisioner` and `IVectorProviderDescriptor` register once per provider (non-generic, singleton); `.Build()` also registers one `VectorCollectionReadinessProbe` (`vector-store-qdrant-{collection}`) per collection. A future second vector provider follows the identical `AddSharedKernel{Provider}()` shape.
- **Orchestration provider** (`AddSharedKernelSemanticKernel()`): registers `IEmbeddingGenerator`, `ISemanticKernel`, `ICompletionProviderDescriptor`, all singleton or scoped per the rule below.
- Engine/model clients are **singletons** (thread-safe, pool their own connections); per-collection and per-request services are **scoped**.
- **A type whose constructor takes a raw `TOptions` (not `IOptions<TOptions>`) must be registered through an explicit factory unwrapping `sp.GetRequiredService<IOptions<TOptions>>().Value`.** `AddValidatedOptions` only ever registers `IOptions<TOptions>` — this was a real, shipped `09.Search` Core-phase defect; do not rediscover it.
- These extensions do **not** self-register `ILogger<T>` or `IClock` — uniformly the consuming host's responsibility platform-wide.
- Misconfiguration fails at `IHost.StartAsync()` via `AddValidatedOptions`' `ValidateOnStart`, naming the missing property — never a silent default, never a first-call failure.

---

## AOT Compatibility

- Interfaces and `sealed record` / `readonly record struct` models over BCL primitives are AOT-safe by construction, and `SharedKernel.AI.Abstractions` stays that way.
- `IVectorRecord.Vector` / `VectorQuery.Vector` are `ReadOnlyMemory<float>`-shaped over BCL types — no boxing, no `object`, no `dynamic`. `System.Numerics.Tensors` is in-box on `net10.0` if vector math is ever needed.
- **`Microsoft.SemanticKernel` is a documented non-AOT-safe dependency** — its function-calling and plugin model is reflection-driven by design. Encapsulating it inside `SharedKernel.AI.SemanticKernel` limits the blast radius to that package's registration and adapter path, exactly the "non-AOT-safe third party placed behind an abstraction" case the root brain's AOT guidance sanctions. `ToolDefinition.ParametersJsonSchema` being a plain string (not a reflected `Type`) on the neutral contract is what keeps that reflection fully contained.
- `Qdrant.Client` is gRPC/protobuf-based; source-generated protobuf is generally trim-friendly. Verify against the real package at Scaffold rather than assuming.
- Any JSON serialization on a hot path prefers an STJ source-generated `JsonSerializerContext` over runtime reflection; any SDK exposing no serializer seam must have that limitation recorded here (the `MeiliSearch` `internal JsonSerializerOptions` finding is the precedent to check for on the vector client).
- No `<IsAotCompatible>true</IsAotCompatible>` on any `.csproj` in this domain, per root policy.

---

## Test Rules

- Unit tests for each package live in its own nested `*.Tests` folder.
- **Never assert on model-generated text.** A test that pins a completion's wording fails on the vendor's next model revision through no fault of this code. Assert on: contract shape, `Result` success/failure and `Error` code, token-usage accounting, streaming chunk assembly and cancellation, filter translation, tenant-scope injection, model-identity/dimension/metric mismatch rejection, and context-window-overflow detection.
- **Never call a paid or live model endpoint from the default test suite.** A live-model test, if one exists at all, is opt-in, environment-variable-gated, and excluded from CI by default. The default `IEmbeddingGenerator` test double is deterministic (a hash-derived vector of the declared dimension) so vector-collection tests need neither a model nor a network.
- **Vector-database behavioural coverage runs against a real container** via `16.Testing`, never a mocked client — filter translation and tenant scoping are only observable against the real engine. Fixtures live in `16.Testing/SharedKernel.Testing.Internal` (non-packable); never hand-roll a competing container setup inside a `.Tests` project.
- **Fail-loud tests are mandatory**: for every rejection path — dimension mismatch, model-identity mismatch, metric mismatch, filter on an undeclared field, missing tenant scope on a tenant-declaring collection, over-ceiling batch size, context-window overflow — assert both that the correct `Error` is returned **and that no I/O occurred**. Construct the adapter with a `null!` client where the SDK ships no substitutable interface, since a clean rejection rather than a `NullReferenceException` is structural proof no call was attempted, paired with a companion test proving the guard is what stopped the I/O (the `09.Search` no-I/O proof technique).
- **When the SDK *does* ship a substitutable interface (`IQdrantClient`), prefer `NSubstitute` over the `null!`-client trick — verified at `SK.10.Core` implementation time.** `Substitute.For<IQdrantClient>()` proves "no I/O occurred" via `client.ReceivedCalls().Should().BeEmpty()` on the rejection path, and the companion "guard-satisfied" test asserts `client.ReceivedCalls().Should().NotBeEmpty()` — **not** an expected `NullReferenceException`. An unconfigured `NSubstitute` substitute does not reliably throw `NullReferenceException` for a `Task<T>`-returning member when `T` is a concrete class with a public parameterless constructor (e.g. a protobuf-generated message type) — NSubstitute's auto-value provider constructs a real, empty instance rather than returning `null`, so the call silently "succeeds" with default-valued output instead of faulting. Where no interface exists at all (e.g. `OpenAI.Embeddings.EmbeddingClient`), the `null!`-client technique still applies, but if the adapter's own `catch (Exception ex) when (...)` clause maps *any* exception (including `NullReferenceException`) into a `Result.Failure`, the companion "guard-satisfied" proof must assert the **distinguishing failure code** (e.g. `intelligence.completion_failed`, the generic engine-fault mapping) rather than an unhandled thrown exception — the NRE is caught and converted, not propagated.
- **Constructing a real instance of a third-party SDK exception/model type with internal-only constructors** (e.g. `System.ClientModel.ClientResultException`, `OpenAI.Chat.ChatTokenUsage`) for status-code-mapping tests: implement the minimal required abstract base (e.g. a `FakePipelineResponse : System.ClientModel.Primitives.PipelineResponse` exposing just `Status`/`ContentStream`/`HeadersCore`/etc.) rather than reaching for reflection-based object fabrication (`FormatterServices.GetUninitializedObject`) — a real, if minimal, subclass is far more robust and is not itself the kind of reflection this domain's hard rule prohibits (that rule targets this domain's own *production* code). Verified: `ChatTokenUsage`'s constructors are all `internal`, so its metadata-extraction path can only be tested via the defensive string-typed fallback or left to a live/integration pass — never fabricated with a guessed-at public constructor.
- **Sanctioned mocking exception**: provider status-code → `Error` mapping assertions (404/401/403/409/429/5xx) may substitute the client via `NSubstitute`. Behavioural, round-trip, and cross-provider conformance coverage is never mocked.
- **Every vector-database provider runs the same behavioural conformance suite** over the same fixed corpus and must produce identical result sets (record identity, count, filter matches — not `Score`, which is explicitly non-portable across providers per its own contract note). The interface makes adding a member a compile error on every provider; only a shared suite makes semantic drift visible. Currently this is `SharedKernel.AI.Qdrant`'s own conformance suite running solo. The suite is written so a future second vector provider slots into the identical shared-corpus/shared-assertions shape rather than needing a redesign.
- Options-validation and DI-registration tests need no container: valid config registers; a missing required field fails at startup naming the property; provider-exclusive contracts resolve **only** from their own provider's builder; gated raw-client accessors do not resolve unless the opt-in was called.
- Each provider's test project carries a sibling-independence check for `using SharedKernel.AI.{OtherProvider}` — matched against `using`-directive **lines** (trimmed prefix), never a whole-file substring search. Authoritative enforcement is `00.Governance`'s topology rules.
- `InternalsVisibleTo` from each provider package to its own nested `.Tests` project keeps filter translators, request validators, and descriptors `internal` while remaining directly unit-testable.
- `ISemanticKernel` tests assert token-usage accounting, `FinishReason` mapping, tool-call round-tripping (constructing a `ToolCallResult` and confirming `.ToMessage()` shape), streaming chunk assembly/cancellation, and `ContextWindowExceeded` rejection — never on generated wording, per the rule above.
- **Qdrant real-container conformance test corpora must use Qdrant-legal record ids — an unsigned 64-bit integer string or a canonical (`"D"`-format) UUID, never a mnemonic label** (verified at `SK.10.Tests`, T-03): `QdrantRecordMapper.ToPointId` rejects anything else as `IntelligenceErrors.InvalidRecordId` before any I/O, so a corpus seeded with ids like `"a1"`/`"wait-1"` fails at seed time with no useful diagnostic pointing at the real cause. Use short numeric-string constants (e.g. `IdA1 = "101"`) if a mnemonic grouping is still wanted for assertion readability.
- **Negative-compile probe (Published-phase `consumer-verify`) — the mechanically honest way to prove "type X cannot be named from this compilation unit," verified for this domain (2026-07-24), mirroring `09.Search`'s identical Published-phase technique:** temporarily append a disallowed `using` plus a bare field declaration of the sibling provider's exclusive type directly into the real `consumer-verify.{Provider}/Program.cs` — with **no** matching `<ProjectReference>` added to that project's `.csproj` — run `dotnet build`, capture the real compiler diagnostic, then immediately revert the probe. For `10.Intelligence` this produced a genuine `CS0234` ("the type or namespace name '{Provider}' does not exist in the namespace 'SharedKernel.AI'") in both directions (`consumer-verify.Qdrant` cannot name `SharedKernel.AI.SemanticKernel.Raw.IKernelRawClientAccessor`; `consumer-verify.SemanticKernel` cannot name `SharedKernel.AI.Qdrant.Raw.IQdrantRawClientAccessor`) — a single `CS0234` was sufficient proof; a second cascading `CS0246` on the field's own type did not additionally fire in this domain's top-level-statement harness shape, unlike `09.Search`'s two-diagnostic transcript, and one genuine compiler error is enough to prove the claim.
- **`QdrantClient` and `OpenAIClient` construction is lazy — no live server or model endpoint is required for a `consumer-verify` harness to prove DI composition.** Neither SDK's client constructor issues an eager network call (`QdrantClient` only builds a gRPC channel; `OpenAIClient` only wires a `System.ClientModel` pipeline transport), so every Published-phase DI-resolution/singleton-lifetime/raw-client-gating/`OptionsValidationException` surface runs against a placeholder host/API-key with zero Docker or live-endpoint dependency — the real-backend guard stays exclusively `SK.10.Tests`' job (`T-03`'s real-`Testcontainers.Qdrant` conformance suite; SemanticKernel's opt-in, environment-variable-gated live-model tests, never run by default).
- **When asserting on an outbound HTTP request body against a real SDK client with no interface seam (e.g. `OpenAI.Embeddings.EmbeddingClient` wired through `FakeHttpMessageHandler`), capture the request body *inside* the response factory delegate — never read `HttpRequestMessage.Content` after the outer SDK call has returned.** `System.ClientModel`'s pipeline (and likely other `HttpClient`-pipeline-based SDKs) disposes the request's content stream once the call completes, so a post-hoc `handler.Requests[0].Content!.ReadAsStringAsync()` throws `ObjectDisposedException` — `handler.EnqueueResponse(request => { capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); return ...; })` captures it at the one moment it is guaranteed still alive.

