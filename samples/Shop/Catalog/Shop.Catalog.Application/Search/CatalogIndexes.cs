namespace Shop.Catalog.Application.Search;

/// <summary>Names of the catalog's search indexes, vector collection and file store.</summary>
public static class CatalogIndexes
{
    /// <summary>The storefront index (Meilisearch).</summary>
    public const string Storefront = "products";

    /// <summary>The back-office index (Elasticsearch).</summary>
    public const string BackOffice = "products-admin";

    /// <summary>The semantic-search vector collection (Qdrant).</summary>
    public const string Vectors = "product-vectors";

    /// <summary>The tenant file store holding product images (S3).</summary>
    public const string ImageStore = "product-images";
}
