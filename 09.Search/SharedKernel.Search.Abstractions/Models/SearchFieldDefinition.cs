namespace SharedKernel.Search.Abstractions.Models;

/// <summary>Declares one field of a <see cref="SearchIndexDefinition"/> and the roles it may play.</summary>
/// <remarks>
/// <b>Six kinds, four booleans, and nothing finer — the type to guard hardest in review.</b> There is
/// no analyzer, tokenizer, normalizer, or language knob on this neutral surface, and there never will
/// be — that is where a neutral mapping DSL lies most convincingly and most harmfully. Anything finer
/// than <see cref="SearchFieldKind"/> plus the four role booleans is a per-provider settings document
/// applied at deploy time, versioned and fingerprinted. Every future "just one more knob" request is a
/// lie about the other engine.
/// </remarks>
public sealed record SearchFieldDefinition
{
    /// <summary>Gets the field name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the field kind.</summary>
    public required SearchFieldKind Kind { get; init; }

    /// <summary>Gets a value indicating whether this field participates in free-text search.</summary>
    public bool Searchable { get; init; }

    /// <summary>Gets a value indicating whether this field may appear in a <see cref="SearchFilter"/>.</summary>
    public bool Filterable { get; init; }

    /// <summary>Gets a value indicating whether this field may appear in a <see cref="SearchSort"/>.</summary>
    public bool Sortable { get; init; }

    /// <summary>Gets a value indicating whether this field may be requested as a facet.</summary>
    public bool Facetable { get; init; }
}
