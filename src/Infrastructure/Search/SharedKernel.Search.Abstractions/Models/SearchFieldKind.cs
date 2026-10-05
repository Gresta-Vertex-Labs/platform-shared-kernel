namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The closed set of field kinds a <see cref="SearchFieldDefinition"/> may declare — deliberately six
/// values and nothing finer (no analyzer, tokenizer, normalizer, or language knob).
/// </summary>
public enum SearchFieldKind
{
    /// <summary>Full-text analysed content (ElasticSearch <c>text</c> / Meilisearch searchable attribute).</summary>
    Text = 0,

    /// <summary>Exact-match, unanalysed content (ElasticSearch <c>keyword</c>).</summary>
    Keyword = 1,

    /// <summary>A 32-bit integer value.</summary>
    Integer = 2,

    /// <summary>A floating-point/decimal numeric value.</summary>
    Decimal = 3,

    /// <summary>A boolean value.</summary>
    Boolean = 4,

    /// <summary>A point-in-time value with an offset from UTC.</summary>
    DateTimeOffset = 5,
}
