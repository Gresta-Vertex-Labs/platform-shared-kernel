using System.Text.Json.Serialization;
using SharedKernel.Search.Abstractions.Abstractions;

namespace CatalogApi;

/// <summary>
/// Field-name constants for <see cref="ProductDocument"/>.
/// </summary>
/// <remarks>
/// A constants class per document type is the domain's mandated convention: field names are strings in
/// a <c>SearchFilter</c>, a <c>SearchSort</c> and a facet request, so a typo is otherwise a runtime
/// "field not filterable" instead of a build error.
/// </remarks>
public static class ProductFields
{
    public const string DocumentId = "documentId";
    public const string TenantId = "tenantId";
    public const string Name = "name";
    public const string Description = "description";
    public const string Brand = "brand";
    public const string Category = "category";
    public const string Price = "price";
    public const string InStock = "inStock";
    public const string Rating = "rating";
    public const string ReleasedOn = "releasedOn";
}

/// <summary>
/// The storefront product, served by Meilisearch — the BFF/fast provider.
/// </summary>
/// <remarks>
/// Primitive members only, as <see cref="ISearchDocument"/> requires: no strongly-typed ids, no value
/// objects, no nested objects. <see cref="JsonPropertyNameAttribute"/> pins every wire name so the
/// document round-trips identically whichever serializer the engine SDK happens to use.
/// </remarks>
public sealed class ProductDocument : ISearchDocument
{
    [JsonPropertyName(ProductFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(ProductFields.TenantId)]
    public required string TenantId { get; init; }

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

    [JsonPropertyName(ProductFields.InStock)]
    public required bool InStock { get; init; }

    [JsonPropertyName(ProductFields.Rating)]
    public required double Rating { get; init; }

    [JsonPropertyName(ProductFields.ReleasedOn)]
    public required DateTimeOffset ReleasedOn { get; init; }
}

/// <summary>Field-name constants for <see cref="OrderLineDocument"/>.</summary>
public static class OrderLineFields
{
    public const string DocumentId = "documentId";
    public const string TenantId = "tenantId";
    public const string ProductName = "productName";
    public const string ProductNameSuggest = "productNameSuggest";
    public const string Category = "category";
    public const string Region = "region";
    public const string Quantity = "quantity";
    public const string Revenue = "revenue";
    public const string OrderedAt = "orderedAt";
}

/// <summary>
/// The back-office order line, served by ElasticSearch — the analytics/heavy provider.
/// </summary>
/// <remarks>
/// <see cref="ProductNameSuggest"/> is a separate field from <see cref="ProductName"/> on purpose: a
/// completion field is an FST input, not a searchable text field, and the document has to populate it
/// itself at index time. It is declared on the index by
/// <c>ElasticSearchBuilder.WithCompletionField</c> — see Program.cs.
/// </remarks>
public sealed class OrderLineDocument : ISearchDocument
{
    [JsonPropertyName(OrderLineFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(OrderLineFields.TenantId)]
    public required string TenantId { get; init; }

    [JsonPropertyName(OrderLineFields.ProductName)]
    public required string ProductName { get; init; }

    [JsonPropertyName(OrderLineFields.ProductNameSuggest)]
    public required string ProductNameSuggest { get; init; }

    [JsonPropertyName(OrderLineFields.Category)]
    public required string Category { get; init; }

    [JsonPropertyName(OrderLineFields.Region)]
    public required string Region { get; init; }

    [JsonPropertyName(OrderLineFields.Quantity)]
    public required long Quantity { get; init; }

    [JsonPropertyName(OrderLineFields.Revenue)]
    public required double Revenue { get; init; }

    [JsonPropertyName(OrderLineFields.OrderedAt)]
    public required DateTimeOffset OrderedAt { get; init; }
}
