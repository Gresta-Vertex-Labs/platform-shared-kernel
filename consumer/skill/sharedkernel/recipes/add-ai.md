---
proven-by:
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/ProductVector.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/ProductIndexer.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/SearchQueries.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Products/SuggestDescription.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/Search/CatalogDefinitions.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/Search/CatalogProvisioning.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/CatalogInfrastructure.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogPipelineTests.cs
unproven:
  - Streaming chat completion (CompleteStreamingAsync): the reference platform asks for one short completion
  - Unit-testing a chat completion: the reference platform tests embeddings and vectors only
  - Alias cutover of a vector collection: the reference platform provisions once at startup
---
# Add AI: embeddings, vector search and chat

**Use when:** you search by meaning (embed text, store vectors per tenant, query the nearest ones) or ask a chat
model for text. **Not when:** users search by words, filters and facets; that is add-search.md. Never inject a
`QdrantClient` or a Semantic Kernel `Kernel` yourself.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.AI.Abstractions` | `IEmbeddingGenerator`, `IVectorCollection<T>`, `IVectorRecord`, `ISemanticKernel`, the collection definition builder |
| Infrastructure | `SharedKernel.AI.Qdrant` | Vector collections in Qdrant |
| Infrastructure | `SharedKernel.AI.SemanticKernel` | Embeddings and chat over an OpenAI-compatible endpoint |
| Tests | `SharedKernel.AI.Testing` | Deterministic in-memory embeddings, vectors and chat |

## Steps

1. **Application: the vector record.** A record carries its id, the vector, the model that produced it and its
   metadata. Metadata field names are constants.

```csharp
using SharedKernel.AI.Abstractions.Models;

public static class ProductVectorFields
{
    public const string TenantId = "tenantId";
    public const string Name = "name";
    public const string Category = "category";
}

public sealed record ProductVector(
    string Id,
    ReadOnlyMemory<float> Vector,
    string ModelId,
    IReadOnlyDictionary<string, VectorValue> Metadata
) : IVectorRecord;
```

2. **Application: embed and upsert when the aggregate changes.** Every call returns a `Result`; the model id travels
   with the vector, so a vector from another model is refused.

```csharp
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;

var embedded = await embeddings.EmbedAsync($"{product.Name}. {product.Category}. {product.Description}", cancellationToken);
if (embedded.IsFailure)
{
    return Result.Failure(embedded.Error);
}

var vector = new ProductVector(
    id,
    embedded.Value.Vector,
    embedded.Value.ModelId,
    new Dictionary<string, VectorValue>
    {
        [ProductVectorFields.TenantId] = VectorValue.From(tenant),
        [ProductVectorFields.Name] = VectorValue.From(product.Name),
        [ProductVectorFields.Category] = VectorValue.From(product.Category),
    }
);
var upserted = await vectors.UpsertAsync(vector, TenantScope.For(product.TenantId), cancellationToken);
return upserted.IsFailure ? Result.Failure(upserted.Error) : Result.Success();
```

3. **Application: query by meaning, scoped to the caller's tenant.**

```csharp
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;

public sealed class SemanticSearchHandler(
    IRequestContext caller,
    IEmbeddingGenerator embeddings,
    IVectorCollection<ProductVector> vectors
) : IQueryHandler<SemanticSearchQuery, IReadOnlyList<SemanticHit>>
{
    public async Task<Result<IReadOnlyList<SemanticHit>>> Handle(SemanticSearchQuery query, CancellationToken cancellationToken)
    {
        var tenant = CallerTenant.Of(caller);   // Forbidden when the caller has no tenant
        if (tenant.IsFailure)
        {
            return Result<IReadOnlyList<SemanticHit>>.Failure(tenant.Error);
        }

        var embedded = await embeddings.EmbedAsync(query.Question, cancellationToken);
        if (embedded.IsFailure)
        {
            return Result<IReadOnlyList<SemanticHit>>.Failure(embedded.Error);
        }

        var results = await vectors.QueryAsync(
            new VectorQuery
            {
                Vector = embedded.Value.Vector,
                ModelId = embedded.Value.ModelId,
                Limit = Math.Clamp(query.Limit, 1, 20),
            },
            TenantScope.For(tenant.Value),
            cancellationToken
        );
        // ...map results.Value.Hits: hit.Record.Id, hit.Record.Metadata[...].AsString, hit.Score...
    }
}
```

4. **Application: ask the chat model.** `ISemanticKernel.CompleteAsync` takes the messages and an output budget and
   returns the message and its token usage:

```csharp
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Primitives.Results;

var completion = await kernel.CompleteAsync(
    new CompletionRequest
    {
        Messages =
        [
            new ChatMessage { Role = ChatRole.System, Content = "You write one short, factual marketing sentence for an online shop." },
            new ChatMessage { Role = ChatRole.User, Content = $"Product: {product.Name} by {product.Brand}, category {product.Category}." },
        ],
        MaxOutputTokens = 60,
    },
    cancellationToken
);
if (completion.IsFailure)
{
    return Result<DescriptionSuggestion>.Failure(completion.Error);
}
// completion.Value.Message.Content, completion.Value.TokenUsage.PromptTokens / CompletionTokens
```

5. **Infrastructure: declare the collection, register both providers, provision.** The collection is bound to one
   embedding model and its dimension; keep both in your own options and use the same values for the endpoint.

```csharp
using SharedKernel.AI.Abstractions.Models;

public static Action<VectorCollectionDefinitionBuilder> Vectors(string embeddingModel, int dimension) =>
    collection =>
        collection
            .EmbeddingModel(embeddingModel, dimension)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field(ProductVectorFields.TenantId, VectorFieldKind.String, filterable: true)
            .Field(ProductVectorFields.Name, VectorFieldKind.String)
            .Field(ProductVectorFields.Category, VectorFieldKind.String, filterable: true)
            .TenantField(ProductVectorFields.TenantId);
```

```csharp
using SharedKernel.AI.Qdrant.Extensions;
using SharedKernel.AI.SemanticKernel.Extensions;

services.AddSharedKernelSemanticKernel(configuration).Build();
services
    .AddSharedKernelQdrant(configuration)
    .AddCollection<ProductVector>(CatalogIndexes.Vectors, CatalogDefinitions.Vectors(embedding.Model, embedding.Dimension))
    .Build();
```

   Create the collection before traffic with a hosted service: build the `VectorCollectionDefinition` with a
   `new VectorCollectionDefinitionBuilder(name)` and the same configure method, then call
   `IVectorCollectionProvisioner.EnsureCollectionAsync(definition, ct)` and stop the host on a failure.

6. **Api/Worker:** `builder.Services.AddHealthChecks().AddSharedKernelReadiness()` maps the Qdrant probe.

7. **Configuration:** `Intelligence:Qdrant` and `Intelligence:SemanticKernel`, plus your own section for the
   collection's model id and dimension. The keys are in each provider's README.

## Test it

Register deterministic in-memory doubles with the same definition:

```csharp
using SharedKernel.Testing.Intelligence;

services.AddInMemoryEmbeddingGenerator(EmbeddingModel, 16);
services.AddInMemoryVectorCollection<ProductVector>(
    CatalogDefinitions.Build(CatalogIndexes.Vectors, CatalogDefinitions.Vectors(EmbeddingModel, 16))
);
```

```csharp
using FluentAssertions;

var hits = await harness.SendAsync(
    new SemanticSearchQuery("Mechanical keyboard. Peripherals. Hot-swappable switches, aluminium case.")
);

hits.IsSuccess.Should().BeTrue();
hits.Value[0].Id.Should().Be(id);
```

`CatalogDefinitions.Build` is the service's own helper around `VectorCollectionDefinitionBuilder.Build()`.
`InMemoryVectorCollection<T>.WasUpserted(id)` asserts a write. A chat double exists (`AddInMemorySemanticKernel()`),
but the reference platform does not test a completion; see the `SharedKernel.AI.Testing` README. The pipeline
harness is in write-tests.md.

## Check

- **SK0026**: a `QdrantClient` or Semantic Kernel `Kernel` injected in your code; inject the
  `SharedKernel.AI.Abstractions` contracts.
- **SK0027**: a literal collection name, model id or metadata field name; use constants. A typo'd model id embeds
  under a phantom model with no engine error.
- **SK0030**: an `UpsertAsync` or `DeleteAsync` whose `Result` nobody reads.
- A vector whose model id or dimension does not match the collection's definition is a failure `Result`, not a
  silent write.

## Read next

- `SharedKernel.AI.Abstractions`
- `SharedKernel.AI.Qdrant`
- `SharedKernel.AI.SemanticKernel`
- `SharedKernel.AI.Testing`
