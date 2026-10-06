using SharedKernel.AI.Abstractions.Models;

namespace Shop.Catalog.Application.Search;

/// <summary>Metadata field names of <see cref="ProductVector"/>.</summary>
public static class ProductVectorFields
{
    public const string TenantId = "tenantId";
    public const string Name = "name";
    public const string Category = "category";
}

/// <summary>A product's embedding in the semantic-search collection. The id is the product id.</summary>
public sealed record ProductVector(
    string Id,
    ReadOnlyMemory<float> Vector,
    string ModelId,
    IReadOnlyDictionary<string, VectorValue> Metadata
) : IVectorRecord;
