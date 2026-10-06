using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using Shop.Catalog.Application.Search;

namespace Shop.Catalog.Infrastructure.Search;

/// <summary>The embedding model the vector collection is bound to (<c>Catalog:Embeddings</c>).</summary>
public sealed class CatalogEmbeddingOptions
{
    public const string SectionName = "Catalog:Embeddings";

    /// <summary>The embedding model id; it must match <c>Intelligence:SemanticKernel:EmbeddingModelId</c>.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>The model's vector dimension.</summary>
    public int Dimension { get; set; }
}

/// <summary>
/// Creates the search indexes and the vector collection before the host serves traffic. Idempotent: an existing index
/// with the same declaration is left alone, a different one fails startup.
/// </summary>
public sealed class CatalogProvisioning(
    IEnumerable<ISearchIndexProvisioner> searchProvisioners,
    IEnumerable<ISearchProviderDescriptor> searchProviders,
    IVectorCollectionProvisioner vectorProvisioner,
    IOptions<CatalogEmbeddingOptions> embeddings
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SearchIndexDefinition[] definitions =
        [
            CatalogDefinitions.Build(CatalogIndexes.Storefront, CatalogDefinitions.Storefront),
            CatalogDefinitions.Build(CatalogIndexes.BackOffice, CatalogDefinitions.BackOffice),
        ];

        // Each provider gets only the indexes it was registered for (ISearchProviderDescriptor.RegisteredIndexes);
        // the provisioner and descriptor sequences are registered pairwise, one pair per provider.
        foreach (var (provisioner, provider) in searchProvisioners.Zip(searchProviders))
        {
            foreach (
                var definition in definitions.Where(d =>
                    provider.RegisteredIndexes.Contains(d.Name)
                )
            )
            {
                var ensured = await provisioner.EnsureIndexAsync(definition, cancellationToken);
                if (ensured.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"{provider.ProviderName} could not provision '{definition.Name}': {ensured.Error.Message}"
                    );
                }
            }
        }

        var collection = CatalogDefinitions.Build(
            CatalogIndexes.Vectors,
            CatalogDefinitions.Vectors(embeddings.Value.Model, embeddings.Value.Dimension)
        );
        var vectors = await vectorProvisioner.EnsureCollectionAsync(collection, cancellationToken);
        if (vectors.IsFailure)
        {
            throw new InvalidOperationException(
                $"Could not provision '{collection.Name}': {vectors.Error.Message}"
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
