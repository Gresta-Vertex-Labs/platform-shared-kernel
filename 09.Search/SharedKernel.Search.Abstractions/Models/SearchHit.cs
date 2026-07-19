using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A single search result — the matched document, its rank, and any highlighted fragments.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <b>There is no <c>Score</c> — the design's loudest single omission.</b> Meilisearch bucket-sorts
/// through ordered ranking rules while ElasticSearch computes BM25; the numbers share no scale, no
/// range, and no monotonicity guarantee. <see cref="Rank"/> (the 0-based ordinal within this result
/// page, as the engine returned it) carries every portable ordering property a caller actually needs.
/// A consumer who genuinely needs a score is, by definition, on a provider-exclusive path.
/// </remarks>
public sealed record SearchHit<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>Gets the matched document.</summary>
    public required TDocument Document { get; init; }

    /// <summary>Gets the 0-based ordinal of this hit within its result page, as the engine returned it.</summary>
    public required int Rank { get; init; }

    /// <summary>
    /// Gets the highlighted fragments per field, or an empty dictionary when highlighting was not
    /// requested or no field matched.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Highlights { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}
