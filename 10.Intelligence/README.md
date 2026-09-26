<div align="center">

# SharedKernel Intelligence

**Embeddings, vector retrieval and LLM orchestration for multi-tenant .NET services — one neutral contract over
Qdrant and Semantic Kernel, where every vector is bound to the model that produced it, every call reports what it
cost, and a provider swap is a build error instead of a production surprise.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Qdrant](https://img.shields.io/badge/Qdrant-v1.16%2B-DC244C)](SharedKernel.AI.Qdrant/README.md)
[![Semantic Kernel](https://img.shields.io/badge/Semantic%20Kernel-1.78-5C2D91)](SharedKernel.AI.SemanticKernel/README.md)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 3](https://img.shields.io/badge/packages-3-informational)

[Packages](#the-packages) · [Quick start](#quick-start) · [Guarantees](#what-you-can-rely-on) · [Readiness](#readiness) · [Testing](#testing)

</div>

---

## The packages

| Package | Tier | What it is | Depends on |
| --- | --- | --- | --- |
| [`SharedKernel.AI.Abstractions`](SharedKernel.AI.Abstractions/README.md) | Abstractions | The contracts application code is written against — `IEmbeddingGenerator`, `IVectorCollection<TRecord>`, `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`, `ISemanticKernel`, `ICompletionProviderDescriptor`, the closed 8-node `VectorFilter` AST, `VectorCollectionDefinition`, `VectorCollectionReadinessProbe`. **Zero third-party NuGet dependencies.** | `SharedKernel.Primitives`, `SharedKernel.Execution` |
| [`SharedKernel.AI.Qdrant`](SharedKernel.AI.Qdrant/README.md) | Adapter | The vector-database provider — collections, payload filters, aliases for zero-downtime re-embedding; Qdrant-only hybrid (dense + sparse) queries and quantization profiles. | `Qdrant.Client` |
| [`SharedKernel.AI.SemanticKernel`](SharedKernel.AI.SemanticKernel/README.md) | Adapter | The embedding and chat-completion provider over OpenAI-compatible endpoints, with streaming and caller-executed tool calls. | `Microsoft.SemanticKernel` |

Reference `.Abstractions` from application code and the provider packages from the composition root only. The two
providers never reference each other; a composition root wired with one cannot even name the other's exclusive
types. Versions come from the consumer's single `SharedKernelVersion`.

---

## Quick start

```csharp
builder.Services
    .AddSharedKernelSemanticKernel(builder.Configuration)          // "Intelligence:SemanticKernel"
    .Build();

builder.Services
    .AddSharedKernelQdrant(builder.Configuration)                  // "Intelligence:Qdrant"
    .AddCollection<ProductChunk>("product-chunks", c => c
        .EmbeddingModel("text-embedding-3-small", dimension: 1536)
        .DistanceMetric(VectorDistanceMetric.Cosine)
        .TenantField("tenantId")
        .Field("tenantId", VectorFieldKind.String, filterable: true)
        .Field("status", VectorFieldKind.String, filterable: true))
    .Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // maps vector-store-qdrant-product-chunks
```

Application code sees only the neutral contracts:

```csharp
public sealed class ProductRetrieval(IEmbeddingGenerator embeddings, IVectorCollection<ProductChunk> chunks)
{
    public async Task<Result<VectorQueryResults<ProductChunk>>> FindAsync(
        TenantId tenantId, string question, CancellationToken ct)   // SharedKernel.Execution.Tenancy
    {
        var embedded = await embeddings.EmbedAsync(question, ct);
        if (embedded.IsFailure)
            return Result<VectorQueryResults<ProductChunk>>.Failure(embedded.Error);

        var query = new VectorQuery
        {
            Vector = embedded.Value.Vector,
            ModelId = embedded.Value.ModelId,                      // checked against the collection before any I/O
            Filter = VectorFilter.Eq("status", "active"),
            Limit = 5,
        };

        return await chunks.QueryAsync(query, TenantScope.For(tenantId), ct);
    }
}
```

---

## What you can rely on

- **A vector is bound to its model.** Every write and query is checked against the collection's declared embedding
  model, dimension and distance metric **before any I/O**. No engine can detect a same-dimension, different-model
  mix; this contract does, and returns `intelligence.embedding_model_mismatch` instead of confidently wrong results.
- **Tenant scope is a separate, mandatory parameter.** Every read and filtered write takes
  `SharedKernel.Execution.Tenancy.TenantScope`. It is added as the outermost filter clause, never mixed into the
  caller's filter. A collection that declares a tenant field and is called with `TenantScope.Global` fails with
  `intelligence.tenant_scope_missing` and sends nothing.
- **Cost is visible.** `TokenUsage` is on every embedding and completion result. Nothing retries a completion by
  default — a retry re-bills and re-rolls the answer; `.WithBoundedRetry(...)` is the explicit opt-in.
- **The kernel never runs your tools.** A `ToolCallsRequested` result hands the tool calls back to the caller, who
  runs them and sends a follow-up request. No completion is ever cached.
- **Expected failures are `Result` values** from `IntelligenceErrors`. The two streaming reads (`ScrollAsync`,
  `CompleteStreamingAsync`) return `IAsyncEnumerable<T>` and throw `IntelligenceStreamException` mid-stream.
- **Content is never logged.** Prompts, completions, retrieved metadata and raw vectors never appear in a log
  message, an `Error` or a telemetry tag. Telemetry runs on the `SharedKernel.AI` source and meter, wired by
  `WithIntelligenceTelemetry()` in `SharedKernel.ServiceDefaults`.
- **`Score` is provider- and metric-specific.** Cosine is bounded, dot product is not, Euclidean is
  smaller-is-better. Never persist a score or reuse a threshold across a provider or metric change; `Rank` is the
  portable ordering.

---

## Readiness

`SharedKernel.AI.Qdrant` registers one `IReadinessProbe` per collection, named `vector-store-qdrant-{collection}`.
It is ready when the server answers, the collection is addressable with this service's credentials and a query
succeeds; a write backlog never fails it. `AddSharedKernelReadiness()` maps every probe to a `ready` health check.
There is no LLM readiness probe: the only honest check is a real, billed completion call.

---

## Testing

Unit tests use [`SharedKernel.AI.Testing`](../16.Testing/SharedKernel.AI.Testing/README.md): in-memory doubles for
every neutral contract, including a deterministic embedding generator that derives its vectors from a hash of the
text, so no model, network or vector database is needed. The Qdrant behavioural conformance suite in this repo runs
against a real container (`qdrant/qdrant:v1.16.0`).

---

## Further reading

- [`CLAUDE.md`](CLAUDE.md) — the full interface contracts, domain invariants and implementation rules.
- [`state-map.md`](state-map.md) — phase history, including the Milvus retraction and the WO-086 refactor.
