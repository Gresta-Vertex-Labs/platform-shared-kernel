using System.ComponentModel.DataAnnotations;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Instant;

/// <summary>
/// A Meilisearch-exclusive instant/type-ahead search request — typo tolerance, prefix matching, and
/// crop markers, none of which are portable to ElasticSearch.
/// </summary>
public sealed record InstantSearchRequest
{
    /// <summary>Gets the free-text query.</summary>
    public required string FreeText { get; init; }

    /// <summary>Gets the structured filter predicate, or <see langword="null"/> for no filter.</summary>
    public SearchFilter? Filter { get; init; }

    /// <summary>Gets the maximum number of hits to return.</summary>
    [Range(1, 50)]
    public int Limit { get; init; } = 10;

    /// <summary>Gets the fields free text is matched against, or empty for every searchable field.</summary>
    public IReadOnlyList<string> AttributesToSearchOn { get; init; } = [];

    /// <summary>Gets the highlight request, or <see langword="null"/> for no highlighting.</summary>
    public HighlightRequest? Highlight { get; init; }

    /// <summary>Gets the crop length, or <see langword="null"/> for the engine default.</summary>
    public int? CropLength { get; init; }

    /// <summary>Gets the crop marker inserted where a value was cropped.</summary>
    public string CropMarker { get; init; } = "…";

    /// <summary>Gets the term-matching strategy.</summary>
    public InstantMatchingStrategy MatchingStrategy { get; init; } = InstantMatchingStrategy.Last;
}
