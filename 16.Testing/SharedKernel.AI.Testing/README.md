# SharedKernel.AI.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **In-memory doubles for every `SharedKernel.AI.Abstractions` contract, so embedding, retrieval and completion code
> runs in a unit test without a model, a network or a vector database — deterministically, and with the same model,
> dimension and tenant checks as production.**

| You get | So that |
| --- | --- |
| `InMemoryEmbeddingGenerator` with hash-derived vectors | The same text always yields the same vector, on every machine, with no model call |
| `InMemoryVectorCollection<TRecord>` with exact cosine, dot-product and Euclidean scoring | Retrieval code gets real similarity ranking, `MinScore` and `Limit` behaviour |
| Model-identity, dimension and `TenantScope` checks | A model upgrade without a re-embed, or a global query on a tenant collection, fails in the unit test |
| `InMemorySemanticKernel` that replays scripted responses | Completion and streaming code is tested without generating text, and every prompt is recorded |
| Provisioner and provider-descriptor doubles | Collection setup, cutover and pre-flight limits are testable |
| `Add…()` DI extensions | One call swaps the real registration in a test host |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.AI.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Add it to a **test project** only. A production project that references it fails the architecture rule
`TestingNeverReferencedByProduction`.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.AI.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions` (no Qdrant client, no Semantic Kernel) |
| Namespaces | `SharedKernel.Testing.Intelligence` |

## Quick start

```csharp
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Intelligence;
using Xunit;

public sealed class ProductRetrievalTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0a0a0a0a-0000-4000-8000-00000000000a"));

    private static VectorCollectionDefinition Definition() =>
        new VectorCollectionDefinitionBuilder("product-chunks")
            .EmbeddingModel("test-model", dimension: 8)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field("tenant_id", VectorFieldKind.String, filterable: true)
            .TenantField("tenant_id")
            .Build()
            .Value;

    [Fact]
    public async Task Indexed_chunks_are_retrieved_for_their_tenant()
    {
        var embeddings = new InMemoryEmbeddingGenerator("test-model", dimension: 8);
        var vectors = new InMemoryVectorCollection<ProductChunk>(Definition());
        var service = new ProductSearchService(embeddings, vectors);   // your class

        await service.IndexAsync("sku-1", "Blue steel anvil", TenantScope.For(Tenant), CancellationToken.None);
        var hits = await service.SearchAsync("Blue steel anvil", TenantScope.For(Tenant), CancellationToken.None);

        Assert.True(vectors.WasUpserted("sku-1"));
        Assert.Equal("sku-1", hits.Value.Hits[0].Record.Id);
        Assert.Equal(2, embeddings.EmbeddedTexts.Count);
    }
}
```

`ProductChunk` implements `IVectorRecord` (`Id`, `Vector`, `ModelId`, `Metadata`); the service stores the tenant in
`Metadata["tenant_id"]` as the tenant's GUID string. Identical text embeds to an identical vector, so the query above
finds the indexed chunk first.

## How it works

- **Embeddings.** Each vector is seeded from `SHA-256(ModelId + text)` and has `Dimension` components in `[-1, 1)`.
  Vectors are stable but carry **no meaning**: similar texts are not near each other. Token usage counts
  whitespace-separated words as prompt tokens.
- **Vector writes.** Upserts are keyed by `Id` and queryable at once. A blank id, a record `ModelId` different from the
  definition's `EmbeddingModelId`, or a wrong vector length fails (per item in `UpsertManyAsync`, which never collapses
  into an outer failure). Deletes are idempotent.
- **Queries.** The query's `ModelId` and vector length are checked first, then the tenant scope. Scores are exact for
  the definition's `DistanceMetric`: cosine and dot product rank descending, Euclidean ascending; `MinScore` respects
  that direction. Only queries that pass validation are recorded in `QueriedVectors`.
- **Tenancy.** On a collection with a `TenantField`, `QueryAsync`, `CountAsync` and `DeleteByFilterAsync` fail with
  `intelligence.tenant_scope_missing` for `TenantScope.Global`, `ScrollAsync` throws `IntelligenceStreamException`, and
  a tenant scope adds an equality clause on that metadata field. `GetAsync` answers another tenant's record with
  `intelligence.record_not_found`.
- **Filters.** Evaluated against `Metadata` by key; a missing key is a non-match (and `false` for `Exists`).
- **Simplified:** `UpsertAsync`, `UpsertManyAsync`, `DeleteAsync` and `DeleteManyAsync` ignore the `TenantScope`
  argument — the record's own metadata decides visibility; filter fields are not checked against the definition's
  `Filterable` flags; `ReturnMetadata`/`ReturnVector` are ignored (the full record is returned);
  `WaitUntilQueryableAsync` always succeeds; receipts carry a fixed `AcceptedAt` (2024-01-01T00:00:00Z); there is no
  readiness probe.
- **Completions.** `InMemorySemanticKernel` never generates text. `CompleteAsync` dequeues the next
  `EnqueueResponse` result or fails with `intelligence.completion_failed`; `CompleteStreamingAsync` dequeues the next
  scripted chunk list (or failure) and yields nothing when the queue is empty. Every request lands in `SentRequests`.
- **Independent fakes.** No fake reads another's state; pass the same definition to each when they must agree.
- **Lifetime.** `AddInMemoryVectorCollection<TRecord>` registers a **singleton** (production registers collections
  scoped), so the recorded history outlives the scope the system under test ran in. All other registrations are
  singletons too. Every fake is thread-safe.

## Recipes

### 1. Script a completion and assert the prompt

```csharp
var kernel = new InMemorySemanticKernel();
kernel.EnqueueResponse(new CompletionResult
{
    Message = new ChatMessage { Role = ChatRole.Assistant, Content = "Ships in 2 days." },
    ModelId = "test-chat",
    TokenUsage = new TokenUsage { PromptTokens = 12, CompletionTokens = 4, TotalTokens = 16 },
    FinishReason = CompletionFinishReason.Stop,
});

var answer = await new SupportAssistant(kernel).AnswerAsync("When will it ship?", CancellationToken.None);

Assert.Equal("Ships in 2 days.", answer);
var request = Assert.Single(kernel.SentRequests);
Assert.Contains(request.Messages, m => m.Role == ChatRole.User && m.Content.Contains("ship"));
```

### 2. Script a stream that fails halfway

```csharp
kernel.EnqueueStreamingResponse([new CompletionChunk { DeltaContent = "Ships " }]);
kernel.EnqueueStreamingFailure(IntelligenceErrors.CompletionFailed("test", "stream dropped"));
```

Each `CompleteStreamingAsync` call consumes one queued item: the first call yields the chunk, the second throws
`IntelligenceStreamException` carrying the error.

### 3. Prove a model mismatch is caught

```csharp
var oldModel = new InMemoryEmbeddingGenerator("old-model", dimension: 8);
var vectors = new InMemoryVectorCollection<ProductChunk>(Definition());   // declares "test-model"

var result = await new ProductSearchService(oldModel, vectors)
    .IndexAsync("sku-1", "anvil", TenantScope.For(Tenant), CancellationToken.None);

Assert.Equal("intelligence.embedding_model_mismatch", result.Error.Code);
```

### 4. Test the provider-fault path

```csharp
embeddings.SimulateFailure = true;   // EmbedAsync/EmbedManyAsync → intelligence.engine_fault
vectors.SimulateFailure = true;      // upserts and deletes → intelligence.write_rejected
```

### 5. Guard a prompt against the context window

```csharp
var descriptor = new InMemoryCompletionProviderDescriptor(contextWindowTokens: 1000);

Assert.Equal("intelligence.context_window_exceeded", descriptor.ValidateContextWindow(1500).Error.Code);
```

## Reference

### Registration

| Method | Registers (all singletons) |
| --- | --- |
| `AddInMemoryEmbeddingGenerator(this IServiceCollection, string modelId, int dimension)` | `InMemoryEmbeddingGenerator` and `IEmbeddingGenerator` → the same instance |
| `AddInMemoryVectorCollection<TRecord>(this IServiceCollection, VectorCollectionDefinition definition)` | `InMemoryVectorCollection<TRecord>` and `IVectorCollection<TRecord>` → the same instance; once per record type |
| `AddInMemoryVectorProvisioning(this IServiceCollection, string providerName = "in-memory-fake")` | `IVectorCollectionProvisioner`, `IVectorProviderDescriptor` |
| `AddInMemorySemanticKernel(this IServiceCollection)` | `ISemanticKernel`, `ICompletionProviderDescriptor` (with default arguments) |

### Types

| Type | Implements | Test helpers |
| --- | --- | --- |
| `InMemoryEmbeddingGenerator(string modelId, int dimension)` | `IEmbeddingGenerator` | `EmbeddedTexts`, `SimulateFailure`, `Reset()` |
| `InMemoryVectorCollection<TRecord>(VectorCollectionDefinition)` (`TRecord : class, IVectorRecord`) | `IVectorCollection<TRecord>` | `UpsertedIds`, `DeletedIds`, `QueriedVectors`, `WasUpserted(id)`, `WasDeleted(id)`, `IsQueryable(id)`, `Seed(record)`, `SimulateFailure`, `Reset()` |
| `InMemoryVectorCollectionProvisioner` | `IVectorCollectionProvisioner` | `RegisteredCollectionNames`, `SimulateFailure`, `Reset()` |
| `InMemoryVectorProviderDescriptor(string providerName = "in-memory-fake")` | `IVectorProviderDescriptor` | `MaxBatchSize` (1000), `MaxVectorDimension` (4096), `MaxFilterDepth` (10), `RegisterCollection(name, definition)`, `Reset()` |
| `InMemorySemanticKernel(string providerName = "in-memory-fake")` | `ISemanticKernel` | `EnqueueResponse`, `EnqueueStreamingResponse`, `EnqueueStreamingFailure`, `SentRequests`, `Reset()` |
| `InMemoryCompletionProviderDescriptor(string providerName = "in-memory-fake", int contextWindowTokens = 128000, int maxOutputTokens = 4096)` | `ICompletionProviderDescriptor` | Settable `ProviderName`, `ContextWindowTokens`, `MaxOutputTokens` |

The provisioner's `EnsureCollectionAsync` is idempotent and additive (a changed field kind or `Filterable` flag fails
with `intelligence.collection_definition_conflict`); `CutoverAsync` needs a registered staging collection. The vector
descriptor's `Validate` checks registration, `MaxVectorDimension` and `MaxFilterDepth` only.

### Errors

| Code | Returned when |
| --- | --- |
| `intelligence.embedding_model_mismatch` / `intelligence.dimension_mismatch` | A record or query does not match the definition |
| `intelligence.tenant_scope_missing` | `TenantScope.Global` on a tenant-declaring collection |
| `intelligence.invalid_record_id` | A blank record id |
| `intelligence.record_not_found` | `GetAsync` for a missing id or another tenant's record |
| `intelligence.write_rejected` / `intelligence.engine_fault` | `SimulateFailure` on the collection or provisioner / on the generator |
| `intelligence.completion_failed` | `CompleteAsync` with no scripted response |
| `intelligence.context_window_exceeded` | `ValidateContextWindow` over the configured window |
| `intelligence.collection_not_found` / `intelligence.invalid_query` / `intelligence.filter_depth_exceeded` | Vector descriptor `Validate` |
| `intelligence.collection_definition_conflict` / `intelligence.cutover_failed` | Provisioner paths above |

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.AI.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.AI.Testing/SharedKernel.AI.Testing.Tests),
which prove the six fakes against the `SharedKernel.AI.Abstractions` contract — determinism, scoring direction,
tenant fail-closed, scripted streaming and provisioning. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
for `TestRequestContext` and `InMemoryLogger` (to assert that prompt text is never logged). Retrieval quality and
real model behaviour need the real provider.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Assert that semantically similar texts rank together | Assert on exact-text matches, filters and scoring order of known vectors | Hash-derived vectors have no semantics |
| Rely on the fake to reject a cross-tenant upsert or delete by id | Stamp the tenant into metadata in your code and test reads per tenant | Writes by id ignore the `TenantScope` argument |
| Call `CompleteAsync` without enqueuing a response | `EnqueueResponse` once per expected call | An empty queue returns `intelligence.completion_failed` |
| Put the tenant in metadata as a non-string value | Store the tenant's GUID string (or a `Guid`, which converts to one) | Tenant matching compares string values |
| Share one fake across tests | Create a new instance or call `Reset()` | Recordings and queues are never cleared otherwise |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
