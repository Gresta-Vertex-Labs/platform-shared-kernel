using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.Search.Meilisearch.Tests.Support;

/// <summary>
/// Serializes/deserializes a <see cref="DateTimeOffset"/> as a Unix epoch SECONDS number — matching
/// <c>MeilisearchFilterCompiler</c>'s own <see cref="DateTimeOffset"/> encoding
/// (<c>value.AsDateTimeOffset.ToUnixTimeSeconds()</c>, bare numeric, never an ISO-8601 string).
/// </summary>
/// <remarks>
/// VERIFIED against the real v1.20.0 engine (2026-07-20, T-26): without this converter, STJ's default
/// <see cref="DateTimeOffset"/> handling writes an ISO-8601 STRING, while every Meilisearch filter
/// comparison on a <see cref="SearchValueKind.DateTimeOffset"/> value emits a bare NUMBER — Meilisearch's
/// filter engine is type-aware and a numeric range comparison never matches a string-typed stored
/// attribute, so every <c>Between(createdAt, ...)</c> filter silently returned zero hits. This is
/// exactly the "MUST be matched by the document mapping" warning already documented on
/// <c>src/Infrastructure/Search/CLAUDE.md</c>'s <c>SearchValue</c>/timestamp-encoding note, now confirmed as
/// load-bearing by a real, previously-uncaught test gap — no prior T-13–T-17 test filtered
/// <c>createdAt</c> by range, only sorted by it (which works even on a string-encoded value, since
/// ISO-8601 strings happen to sort lexicographically in date order — masking the mismatch).
/// </remarks>
internal sealed class UnixSecondsDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64());

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.ToUnixTimeSeconds());
}

/// <summary>Field-name constants for <see cref="TestProduct"/> — the shared T-13–T-17/T-26 real-backend test corpus.</summary>
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
/// The shared real-backend test corpus document — one field of every <c>SearchFieldKind</c> so every
/// filter/sort/facet/highlight code path has a field to exercise. <see cref="JsonPropertyNameAttribute"/>
/// pins every wire field name explicitly so document (de)serialization is deterministic regardless of
/// the Meilisearch SDK's own internal (undocumented, hard-coded) camelCase naming policy.
/// </summary>
internal sealed record TestProduct : ISearchDocument
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
    [JsonConverter(typeof(UnixSecondsDateTimeOffsetConverter))]
    public required DateTimeOffset CreatedAt { get; init; }
}
