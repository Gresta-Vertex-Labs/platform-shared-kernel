# SharedKernel.AI.Abstractions

AI / vector-retrieval abstraction contracts for Platform.SharedKernel microservices. Defines `IEmbeddingGenerator` (text-to-vector, batched, token-usage-accounted), `IVectorCollection<TRecord>` (upsert/delete/query/get/count/scroll with mandatory model-identity/dimension/tenant-scope validation before any I/O), `IVectorCollectionProvisioner` (ensure/exists/delete/cutover), `IVectorProviderDescriptor` and `ICompletionProviderDescriptor` (ceilings + zero-I/O pre-flight validation), `ISemanticKernel` (stateless chat/completion orchestration), the closed 8-node `VectorFilter` AST over the closed five-kind `VectorValue` scalar union, plus `VectorCollectionReadinessProbe` and the `IntelligenceErrors` factory. **Abstractions tier, zero third-party NuGet dependencies** — references only `SharedKernel.Primitives` and `SharedKernel.Execution` (for `TenantScope`/`TenantId`). Implemented by `SharedKernel.AI.Qdrant` (vector database) and `SharedKernel.AI.SemanticKernel` (LLM orchestration).

Application code should always inject `IEmbeddingGenerator` / `IVectorCollection<TRecord>` / `IVectorCollectionProvisioner` / `IVectorProviderDescriptor` / `ISemanticKernel` / `ICompletionProviderDescriptor` from this package — never a concrete model SDK or vector-database client type (`QdrantClient`, `Kernel`, an `OpenAIClient`, …) directly.

## Included Types

- `IVectorRecord` — a self-supplied `Id`/`Vector`/`ModelId`/`Metadata` surface every stored record type implements
- `IEmbeddingGenerator` — non-generic, text-only: `EmbedAsync`, `EmbedManyAsync`, both returning unconditional `TokenUsage`
- `IVectorCollection<TRecord>` — write (`UpsertAsync`, `UpsertManyAsync`, `DeleteAsync`, `DeleteManyAsync`, `DeleteByFilterAsync`, `WaitUntilQueryableAsync`), read (`QueryAsync`, `GetAsync`, `CountAsync`), corpus walk (`ScrollAsync`)
- `IVectorCollectionProvisioner` — non-generic, one per provider: `EnsureCollectionAsync`, `CollectionExistsAsync`, `DeleteCollectionAsync`, `CutoverAsync` (readiness is not on this contract — see below)
- `IVectorProviderDescriptor` / `ICompletionProviderDescriptor` — singleton, zero I/O ceiling + pre-flight-validation descriptors, kept deliberately separate (a vector engine's batch/dimension/filter-depth ceilings and an LLM's context-window/output-token ceilings share no honestly-common members)
- `ISemanticKernel` — `CompleteAsync` / `CompleteStreamingAsync`, deliberately stateless: no tool execution, no retry, no caching (see below)
- `VectorFilter` — closed 8-node AST (`Eq`/`Ne`/`In`/`Between`/`Exists`/`All`/`Any`/`Negate`) over the closed five-kind `VectorValue` scalar union (`String`/`Int64`/`Double`/`Boolean`/`DateTimeOffset`)
- `Models/` — `VectorCollectionDefinition` + `VectorFieldDefinition` + `VectorDistanceMetric` + `VectorFieldKind` + `VectorCollectionDefinitionBuilder`, `VectorQuery`, `VectorQueryResults<TRecord>`, `VectorHit<TRecord>`
- `Abstractions/` — `VectorWriteReceipt`, `VectorItemFailure`, `VectorBulkReceipt`, `VectorCollectionCutoverRequest`, `VectorCollectionHealth`, `TokenUsage`, `EmbeddingResult`, `EmbeddingBatchResult`, `ChatRole`, `ChatMessage`, `ToolDefinition`, `ToolCallRequest`, `ToolCallResult`, `CompletionFinishReason`, `CompletionRequest`, `CompletionResult`, `CompletionChunk`
- `IntelligenceErrors` — static `Error` factory covering not-found, validation, conflict, unauthorized, and unexpected outcomes
- `IntelligenceWellKnown` — the domain-local named-constants holder (query-limit defaults, `ActivitySource`/`Meter` names, provider names, OTel tag keys)
- `IntelligenceStreamException` — the `Error`-carrying exception thrown from `ScrollAsync`'s and `CompleteStreamingAsync`'s `MoveNextAsync`, the domain's two documented exceptions to the `Result`-first rule

## Install

```xml
<PackageReference Include="SharedKernel.AI.Abstractions" />
```

Versions come from the consumer's single `SharedKernelVersion`. Add a provider package (`SharedKernel.AI.Qdrant` or `SharedKernel.AI.SemanticKernel`) in the host to actually resolve any of the above — this package ships no DI extensions and no implementation. For unit tests, `SharedKernel.AI.Testing` has in-memory doubles for every contract here.

## Readiness

`IVectorCollectionProvisioner` has no probe member. Each vector provider registers one `VectorCollectionReadinessProbe` (`SharedKernel.Primitives.Health.IReadinessProbe`) per collection, named `vector-store-{provider}-{collection}` (`VectorCollectionReadinessProbe.ProbeNameFor`). It is ready when the store is reachable and the collection is addressable and queryable; a write backlog never fails it. The measured values (`Reachable`, `VectorCount`, `SchemaFingerprint`, …) are in `ReadinessReport.Data` under the probe's `*Key` constants. The host maps every probe to a health check with `services.AddHealthChecks().AddSharedKernelReadiness()` (`SharedKernel.ServiceDefaults`). There is no LLM readiness probe: the only honest check is a real, billed completion call.

## Design principles

- **The seam rule.** `SharedKernel.AI.Abstractions` contains no type that a candidate provider cannot implement completely and correctly. If a capability would force one adapter to throw, degrade, approximate, or no-op, it does not live here — it becomes a provider-package-declared exclusive contract instead (sparse/hybrid vectors and quantization on Qdrant; plugin/planner access on SemanticKernel). Referencing one of these takes a compile-time dependency on that provider package, so a provider swap is a **build error**, never a startup resolution error.
- **`Result`-valued expected failures.** Model-not-found, unauthorized, rate-limited, context-window-exceeded, dimension mismatch, model-identity mismatch, collection-not-found, and missing tenant scope are `Error` values via `IntelligenceErrors` — never thrown exceptions. The only two exceptions are `IVectorCollection<TRecord>.ScrollAsync` and `ISemanticKernel.CompleteStreamingAsync`, which return a bare `IAsyncEnumerable<T>` and surface mid-stream transport faults as `IntelligenceStreamException` from `MoveNextAsync` — mirroring the `06.Persistence`/`08.Storage`/`09.Search` streaming-read precedent.
- **An embedding is meaningless without its model identity.** A vector is only comparable against vectors produced by the same model, at the same dimensionality, with the same normalization. `VectorCollectionDefinition.EmbeddingModelId`/`.Dimension`/`.DistanceMetric` are validated against `IVectorRecord.ModelId`/`.Vector.Length` on every write and against `VectorQuery.ModelId`/`.Vector.Length` on every query — **before any I/O** — because no vector-database engine detects a same-dimension, different-model mix on its own; it just returns confidently, silently wrong similarity scores forever.
- **`TenantScope` (`SharedKernel.Execution.Tenancy`) is a mandatory, separate method parameter — never a filter clause, never a request member.** Build it with `TenantScope.For(tenantId)` from the caller's `TenantId`, or `TenantScope.Global` for a collection that belongs to no tenant; adapters store and match the tenant field as the `TenantId`'s `"D"` string. A tenant predicate travelling through the same filter tree as business predicates can be dropped by a translation bug; a dropped business clause is a bug, a dropped tenant clause is a cross-tenant data leak. Adapters inject it as the outermost `AND` after translating the caller's filter. If the registered `VectorCollectionDefinition` declares a `TenantField` and the caller passes `TenantScope.Global`, the provider fails closed with `IntelligenceErrors.TenantScopeMissing` and performs **no I/O**.
- **`Score` exists — the one deliberate deviation from `09.Search`'s outright score ban.** Similarity score is often genuinely load-bearing for a RAG caller (e.g. "only surface chunks above cosine 0.75") in a way full-text relevance rarely is. `VectorHit<TRecord>.Score` and `VectorQuery.MinScore` both carry the loudest possible XML doc warning: **the scale is provider- and metric-specific.** Cosine similarity is bounded, dot-product is unbounded, Euclidean distance is smaller-is-better — the opposite direction of the other two. Never persist, threshold against a hard-coded constant, or compare `Score` across a provider or metric swap. `VectorHit<TRecord>.Rank` (the 0-based ordinal within the result page) is the portable substitute.
- **No `WriteConsistency`/consistency-level parameter on any write.** Vector engines do not share one write-visibility model (Qdrant has a `wait` boolean on the write; other engines govern visibility by the *query's* consistency level), so a shared enum would be honest on one engine and meaningless on another. `WaitUntilQueryableAsync` is the honest, separate, opt-in deferred barrier instead.
- **Cost and token usage are first-class outputs, never hidden.** `TokenUsage` rides on every `EmbeddingResult`, `EmbeddingBatchResult`, and `CompletionResult` unconditionally. `.Abstractions` exposes **no retry-shaped member anywhere** — a retry on a completion call re-bills and re-rolls a non-deterministic output. If a provider offers one at all, it is an explicit, bounded, opt-in builder call, never automatic.
- **Never promise determinism.** No XML doc, member name, or test asserts that model-generated output is reproducible.
- **Prompt and completion content is never logged.** `ChatMessage.Content`, `CompletionChunk.DeltaContent`, retrieved `IVectorRecord.Metadata` values, and raw vectors are never log-message parameters, `Error` message parameters, or diagnostic tags — only identifiers, model ids, token counts, latencies, and outcome codes are.

## `ISemanticKernel`'s stateless boundary — no tools, no retry, no cache

`ISemanticKernel` deliberately carries exactly two members, `CompleteAsync` and `CompleteStreamingAsync`, and nothing else:

- **No `InvokeToolAsync` or agent/planner-loop member.** When `CompletionResult.FinishReason == CompletionFinishReason.ToolCallsRequested`, the *caller* executes each `ToolCallRequest` against its own business logic, builds a `ToolCallResult`, appends `.ToMessage()` to the growing `CompletionRequest.Messages` list, and issues a follow-up `CompleteAsync` call. Tool execution is arbitrary consumer-owned business logic — invoking it from this layer would require exactly the reflection-driven dynamic dispatch, or a domain-logic reference, this package must never take.
- **No retry member.** A retry re-bills and re-rolls a non-deterministic output. `SharedKernel.AI.SemanticKernel`'s opt-in `.WithBoundedRetry(...)` builder call is the only sanctioned retry path anywhere in this domain, and it is never reachable through `ISemanticKernel` itself.
- **No caching member.** `CompleteAsync` always dispatches a fresh call. `10.Intelligence` may not reference `02.Caching` in any case, and this package must never silently serve a stale completion the caller did not explicitly ask for.

## Worked example — collection definition, embed, upsert, query

```csharp
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;

public sealed class ProductChunkRecord : IVectorRecord
{
    public required string Id { get; init; }
    public required ReadOnlyMemory<float> Vector { get; init; }
    public required string ModelId { get; init; }
    public required IReadOnlyDictionary<string, VectorValue> Metadata { get; init; }
}

public sealed class ProductSearchService(
    IEmbeddingGenerator embeddings,
    IVectorCollection<ProductChunkRecord> collection)
{
    public async Task<Result<VectorQueryResults<ProductChunkRecord>>> SearchAsync(
        TenantId tenantId, string queryText, CancellationToken ct)
    {
        var embedResult = await embeddings.EmbedAsync(queryText, ct);
        if (embedResult.IsFailure)
        {
            return Result<VectorQueryResults<ProductChunkRecord>>.Failure(embedResult.Error);
        }

        var query = new VectorQuery
        {
            Vector = embedResult.Value.Vector,
            ModelId = embedResult.Value.ModelId,
            Filter = VectorFilter.Eq("status", VectorValue.From("active")),
            Limit = 10,
            MinScore = 0.75f,
        };

        return await collection.QueryAsync(query, TenantScope.For(tenantId), ct);
    }
}
```

`VectorCollectionDefinition.Create(...)` (or the fluent `VectorCollectionDefinitionBuilder`) declares the collection's embedding-model identity, vector dimension, and distance metric once, at provisioning time — every subsequent write and query validates against that declaration before any network call, never after.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [10.Intelligence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, provider implementation rules, and AOT posture.
