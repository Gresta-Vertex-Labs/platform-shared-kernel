using SharedKernel.Search.Abstractions.Constants;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>Requests highlighted fragments for the matched fields of a <see cref="SearchRequest"/>.</summary>
/// <remarks>
/// <see cref="FragmentSize"/>/<see cref="MaxFragments"/> map to ElasticSearch's
/// <c>fragment_size</c>/<c>number_of_fragments</c> and to Meilisearch's <c>cropLength</c>/
/// <c>attributesToCrop</c>. The mapping is approximate — Meilisearch crops in words, ElasticSearch in
/// characters — so identical numbers produce visibly different fragments across a provider swap. This
/// is documented, not normalised, because normalising would require guessing an average word length.
/// </remarks>
public sealed record HighlightRequest
{
    /// <summary>Gets the fields to highlight.</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>Gets the tag inserted before each highlighted match.</summary>
    public string PreTag { get; init; } = SearchWellKnown.DefaultHighlightPreTag;

    /// <summary>Gets the tag inserted after each highlighted match.</summary>
    public string PostTag { get; init; } = SearchWellKnown.DefaultHighlightPostTag;

    /// <summary>Gets the approximate fragment size, or <see langword="null"/> for the engine default.</summary>
    public int? FragmentSize { get; init; }

    /// <summary>Gets the maximum number of fragments per field, or <see langword="null"/> for the engine default.</summary>
    public int? MaxFragments { get; init; }
}
