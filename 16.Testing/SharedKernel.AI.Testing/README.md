# SharedKernel.AI.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**In-memory doubles for `SharedKernel.AI.Abstractions`, so embedding, retrieval and completion code runs in a unit
test without a model, a network or a vector database.** The embedding generator derives its vectors from a hash of
the text, so the same input always yields the same vector.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.AI.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Intelligence`.

## Contents

| Type | Stands in for | Notes |
| --- | --- | --- |
| `InMemoryEmbeddingGenerator` | `IEmbeddingGenerator` | Deterministic, hash-derived vectors of the configured dimension; reports its `ModelId`. `EmbeddedTexts`, `SimulateFailure`, `Reset()` |
| `InMemoryVectorCollection<TRecord>` | `IVectorCollection<TRecord>` | Validates every request against its `VectorCollectionDefinition` (model identity, dimension, metric) and scopes every read and filtered write by the mandatory `TenantScope`. `Seed`, `UpsertedIds`, `DeletedIds`, `QueriedVectors`, `WasUpserted(id)`, `WasDeleted(id)`, `SimulateFailure`, `Reset()` |
| `InMemoryVectorCollectionProvisioner` | `IVectorCollectionProvisioner` | Ensure / exists / delete / cutover, recorded in `RegisteredCollectionNames`; `SimulateFailure` |
| `InMemoryVectorProviderDescriptor` | `IVectorProviderDescriptor` | Zero-I/O limits (`MaxBatchSize`, `MaxVectorDimension`, `MaxFilterDepth`) and `Validate`; `RegisterCollection` |
| `InMemorySemanticKernel` | `ISemanticKernel` | Returns scripted completions: `EnqueueResponse`, `EnqueueStreamingResponse`, `EnqueueStreamingFailure`; every request is kept in `SentRequests` |
| `InMemoryCompletionProviderDescriptor` | `ICompletionProviderDescriptor` | Configurable `ContextWindowTokens` / `MaxOutputTokens`, `ValidateContextWindow` |

## Registration

```csharp
services.AddInMemoryEmbeddingGenerator("text-embedding-3-small", dimension: 1536);
services.AddInMemoryVectorCollection<ProductVector>(definition);   // once per record type
services.AddInMemoryVectorProvisioning();                          // provisioner + provider descriptor
services.AddInMemorySemanticKernel();                              // kernel + completion descriptor
```

Every registration is a singleton. For the vector collection that deliberately differs from production (scoped per
collection): the recorded history has to outlive the system under test's scope so the test can assert afterwards.

## Example

```csharp
var embeddings = new InMemoryEmbeddingGenerator("test-model", dimension: 8);
var vectors = new InMemoryVectorCollection<ProductVector>(definition);   // declares "test-model", 8 dimensions
var indexer = new ProductIndexer(embeddings, vectors);

await indexer.IndexAsync(product, TenantScope.For(tenantId), ct);

vectors.WasUpserted(product.Id.ToString()).Should().BeTrue();
embeddings.EmbeddedTexts.Should().ContainSingle();
```

A request whose vector dimension or model identity does not match the definition fails exactly as it does against
Qdrant, so a model upgrade without a re-embed is caught in the unit test.

## Related packages

- References `SharedKernel.AI.Abstractions` only — no Qdrant client, no Semantic Kernel.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) supplies the shared basics (`FakeClock`,
  `InMemoryLogger`, `TestRequestContext`, fakers and assertions).
