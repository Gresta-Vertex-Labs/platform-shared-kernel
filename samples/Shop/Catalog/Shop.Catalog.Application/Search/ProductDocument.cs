using System.Text.Json.Serialization;
using SharedKernel.Search.Abstractions.Abstractions;

namespace Shop.Catalog.Application.Search;

/// <summary>Field names of the product documents, so a filter or facet cannot misspell one.</summary>
public static class ProductFields
{
    public const string DocumentId = "documentId";
    public const string TenantId = "tenantId";
    public const string Sku = "sku";
    public const string Name = "name";
    public const string Description = "description";
    public const string Brand = "brand";
    public const string Category = "category";
    public const string Price = "price";
    public const string Currency = "currency";
}

/// <summary>The storefront's product, served by Meilisearch.</summary>
public sealed class ProductDocument : ISearchDocument
{
    [JsonPropertyName(ProductFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(ProductFields.TenantId)]
    public required string TenantId { get; init; }

    [JsonPropertyName(ProductFields.Sku)]
    public required string Sku { get; init; }

    [JsonPropertyName(ProductFields.Name)]
    public required string Name { get; init; }

    [JsonPropertyName(ProductFields.Description)]
    public required string Description { get; init; }

    [JsonPropertyName(ProductFields.Brand)]
    public required string Brand { get; init; }

    [JsonPropertyName(ProductFields.Category)]
    public required string Category { get; init; }

    [JsonPropertyName(ProductFields.Price)]
    public required double Price { get; init; }

    [JsonPropertyName(ProductFields.Currency)]
    public required string Currency { get; init; }
}

/// <summary>
/// The back office's product, served by Elasticsearch. A separate type from <see cref="ProductDocument"/>: two
/// providers registered for one document type would silently shadow each other.
/// </summary>
public sealed class ProductAdminDocument : ISearchDocument
{
    [JsonPropertyName(ProductFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(ProductFields.TenantId)]
    public required string TenantId { get; init; }

    [JsonPropertyName(ProductFields.Sku)]
    public required string Sku { get; init; }

    [JsonPropertyName(ProductFields.Name)]
    public required string Name { get; init; }

    [JsonPropertyName(ProductFields.Brand)]
    public required string Brand { get; init; }

    [JsonPropertyName(ProductFields.Category)]
    public required string Category { get; init; }

    [JsonPropertyName(ProductFields.Price)]
    public required double Price { get; init; }
}
