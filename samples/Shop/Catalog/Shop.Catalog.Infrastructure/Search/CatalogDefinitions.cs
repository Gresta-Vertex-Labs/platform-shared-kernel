using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Search.Abstractions.Models;
using Shop.Catalog.Application.Search;

namespace Shop.Catalog.Infrastructure.Search;

/// <summary>
/// The shapes of the catalog's indexes and vector collection, declared once and used both to register the providers
/// and to provision the engines at startup.
/// </summary>
public static class CatalogDefinitions
{
    /// <summary>The storefront index (Meilisearch).</summary>
    public static void Storefront(SearchIndexDefinitionBuilder index) =>
        index
            .PrimaryKey(ProductFields.DocumentId)
            .TenantField(ProductFields.TenantId)
            .Field(ProductFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
            .Field(ProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
            .Field(ProductFields.Sku, SearchFieldKind.Keyword, searchable: true, filterable: true)
            .Field(ProductFields.Name, SearchFieldKind.Text, searchable: true)
            .Field(ProductFields.Description, SearchFieldKind.Text, searchable: true)
            .Field(
                ProductFields.Brand,
                SearchFieldKind.Keyword,
                searchable: true,
                filterable: true,
                facetable: true
            )
            .Field(
                ProductFields.Category,
                SearchFieldKind.Keyword,
                filterable: true,
                facetable: true
            )
            .Field(ProductFields.Price, SearchFieldKind.Decimal, filterable: true, sortable: true)
            .Field(ProductFields.Currency, SearchFieldKind.Keyword, filterable: true)
            .Synonym("laptop", "notebook")
            .StopWords("the", "a", "an", "with", "and");

    /// <summary>The back-office index (Elasticsearch).</summary>
    public static void BackOffice(SearchIndexDefinitionBuilder index) =>
        index
            .PrimaryKey(ProductFields.DocumentId)
            .TenantField(ProductFields.TenantId)
            .Field(ProductFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
            .Field(ProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
            .Field(ProductFields.Sku, SearchFieldKind.Keyword, searchable: true, filterable: true)
            .Field(ProductFields.Name, SearchFieldKind.Text, searchable: true)
            .Field(ProductFields.Brand, SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Field(
                ProductFields.Category,
                SearchFieldKind.Keyword,
                filterable: true,
                facetable: true
            )
            .Field(ProductFields.Price, SearchFieldKind.Decimal, filterable: true, sortable: true);

    /// <summary>The semantic-search collection (Qdrant), bound to the embedding model and its dimension.</summary>
    public static Action<VectorCollectionDefinitionBuilder> Vectors(
        string embeddingModel,
        int dimension
    ) =>
        collection =>
            collection
                .EmbeddingModel(embeddingModel, dimension)
                .DistanceMetric(VectorDistanceMetric.Cosine)
                .Field(ProductVectorFields.TenantId, VectorFieldKind.String, filterable: true)
                .Field(ProductVectorFields.Name, VectorFieldKind.String)
                .Field(ProductVectorFields.Category, VectorFieldKind.String, filterable: true)
                .TenantField(ProductVectorFields.TenantId);

    /// <summary>Builds a search definition, failing loudly on an invalid declaration.</summary>
    public static SearchIndexDefinition Build(
        string name,
        Action<SearchIndexDefinitionBuilder> configure
    )
    {
        var builder = new SearchIndexDefinitionBuilder(name);
        configure(builder);
        var definition = builder.Build();
        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"Index '{name}' is invalid: {definition.Error.Message}"
            );
    }

    /// <summary>Builds a vector collection definition, failing loudly on an invalid declaration.</summary>
    public static VectorCollectionDefinition Build(
        string name,
        Action<VectorCollectionDefinitionBuilder> configure
    )
    {
        var builder = new VectorCollectionDefinitionBuilder(name);
        configure(builder);
        var definition = builder.Build();
        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(
                $"Collection '{name}' is invalid: {definition.Error.Message}"
            );
    }
}
