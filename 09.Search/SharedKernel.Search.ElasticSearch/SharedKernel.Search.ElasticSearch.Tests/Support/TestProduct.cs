using System.Text.Json.Serialization;
using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>Field-name constants for <see cref="TestProduct"/> — the shared T-21–T-25 real-backend test corpus.</summary>
internal static class TestProductFields
{
    public const string DocumentId = "documentId";
    public const string TenantId = "tenantId";
    public const string Name = "name";
    public const string Description = "description";
    public const string Status = "status";
    public const string Category = "category";
    public const string Price = "price";
    public const string Stock = "stock";
    public const string InStock = "inStock";
    public const string CreatedAt = "createdAt";
}

/// <summary>
/// The shared real-backend test corpus document — one field of every <see cref="SearchFieldKind"/>.
/// <see cref="JsonPropertyNameAttribute"/> pins every wire field name explicitly so document
/// (de)serialization is deterministic without depending on the ElasticSearch client's default
/// reflection-based STJ naming policy (no <c>JsonSerializerContext</c> is registered for these tests).
/// </summary>
internal sealed class TestProduct : ISearchDocument
{
    [JsonPropertyName(TestProductFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(TestProductFields.TenantId)]
    public required string TenantId { get; init; }

    [JsonPropertyName(TestProductFields.Name)]
    public required string Name { get; init; }

    [JsonPropertyName(TestProductFields.Description)]
    public required string Description { get; init; }

    [JsonPropertyName(TestProductFields.Status)]
    public required string Status { get; init; }

    [JsonPropertyName(TestProductFields.Category)]
    public required string Category { get; init; }

    [JsonPropertyName(TestProductFields.Price)]
    public required double Price { get; init; }

    [JsonPropertyName(TestProductFields.Stock)]
    public required long Stock { get; init; }

    [JsonPropertyName(TestProductFields.InStock)]
    public required bool InStock { get; init; }

    [JsonPropertyName(TestProductFields.CreatedAt)]
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// A <see cref="TestProduct"/> variant carrying an ElasticSearch <c>completion</c>-typed suggest field,
/// used by the <c>ISuggestSearch&lt;TDocument&gt;</c> real-backend tests. A completion field is a
/// separate mapping field populated at index time — it is an FST input, not a searchable text field —
/// so the document type must supply it explicitly.
/// </summary>
internal sealed class SuggestableProduct : ISearchDocument
{
    public const string SuggestField = "nameSuggest";

    [JsonPropertyName(TestProductFields.DocumentId)]
    public required string DocumentId { get; init; }

    [JsonPropertyName(TestProductFields.TenantId)]
    public required string TenantId { get; init; }

    [JsonPropertyName(TestProductFields.Name)]
    public required string Name { get; init; }

    [JsonPropertyName(SuggestField)]
    public required string NameSuggest { get; init; }
}
