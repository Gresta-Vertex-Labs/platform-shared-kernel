using System.Text.Json;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Querying;

/// <summary>
/// Maps a Meilisearch <see cref="global::Meilisearch.ISearchable{T}"/> response onto a neutral
/// <see cref="SearchResults{TDocument}"/>.
/// </summary>
/// <remarks>
/// The adapter always searches with <c>T = JsonElement</c> so it can read each hit's <c>_formatted</c>
/// sibling object for highlighting — a strongly-typed <c>TDocument</c> search would silently discard
/// it, since <c>TDocument</c> declares no <c>_formatted</c> member. Each hit is then deserialized from
/// its <see cref="JsonElement"/> into <typeparamref name="TDocument"/> directly.
/// </remarks>
internal static class MeilisearchResultMapper
{
    private static readonly JsonSerializerOptions DocumentSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyHighlights =
        new Dictionary<string, IReadOnlyList<string>>();

    private static readonly IReadOnlyDictionary<string, FacetResult> EmptyFacets =
        new Dictionary<string, FacetResult>();

    /// <summary>
    /// Maps <paramref name="searchable"/> — pattern-matching the concrete form the caller
    /// <paramref name="expectExact"/> flag says it requested — into a
    /// <see cref="SearchResults{TDocument}"/>. Returns <see cref="SearchErrors.EngineFault"/> on a
    /// mismatch rather than casting blindly.
    /// </summary>
    public static Result<SearchResults<TDocument>> Map<TDocument>(
        global::Meilisearch.ISearchable<JsonElement> searchable,
        bool expectExact,
        int maxFacetValues,
        string? highlightPreTag,
        string providerName)
        where TDocument : class, ISearchDocument
    {
        return (searchable, expectExact) switch
        {
            (global::Meilisearch.PaginatedSearchResult<JsonElement> paginated, true) =>
                Result<SearchResults<TDocument>>.Success(MapPaginated<TDocument>(paginated, maxFacetValues, highlightPreTag)),
            (global::Meilisearch.SearchResult<JsonElement> estimated, false) =>
                Result<SearchResults<TDocument>>.Success(MapEstimated<TDocument>(estimated, maxFacetValues, highlightPreTag)),
            _ => Result<SearchResults<TDocument>>.Failure(SearchErrors.EngineFault(
                providerName,
                "SearchAsync",
                $"Expected a {(expectExact ? "paginated (page/hitsPerPage)" : "offset/limit")} response " +
                $"but received '{searchable.GetType().Name}'.")),
        };
    }

    private static SearchResults<TDocument> MapPaginated<TDocument>(
        global::Meilisearch.PaginatedSearchResult<JsonElement> result, int maxFacetValues, string? highlightPreTag)
        where TDocument : class, ISearchDocument
        => new()
        {
            Hits = MapHits<TDocument>(result.Hits, highlightPreTag),
            TotalHits = result.TotalHits,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = result.Page,
            PageSize = result.HitsPerPage,
            Facets = MapFacets(result.FacetDistribution, result.FacetStats, maxFacetValues),
            Duration = TimeSpan.FromMilliseconds(result.ProcessingTimeMs),
        };

    private static SearchResults<TDocument> MapEstimated<TDocument>(
        global::Meilisearch.SearchResult<JsonElement> result, int maxFacetValues, string? highlightPreTag)
        where TDocument : class, ISearchDocument
    {
        var pageSize = result.Limit;
        var page = pageSize > 0 ? (result.Offset / pageSize) + 1 : 1;

        return new SearchResults<TDocument>
        {
            Hits = MapHits<TDocument>(result.Hits, highlightPreTag),
            TotalHits = result.EstimatedTotalHits,
            Accuracy = TotalHitsAccuracy.Estimated,
            Page = page,
            PageSize = pageSize,
            Facets = MapFacets(result.FacetDistribution, result.FacetStats, maxFacetValues),
            Duration = TimeSpan.FromMilliseconds(result.ProcessingTimeMs),
        };
    }

    private static IReadOnlyList<SearchHit<TDocument>> MapHits<TDocument>(
        IReadOnlyCollection<JsonElement> rawHits, string? highlightPreTag)
        where TDocument : class, ISearchDocument
    {
        var hits = new List<SearchHit<TDocument>>(rawHits.Count);
        var rank = 0;
        foreach (var raw in rawHits)
        {
            var document = raw.Deserialize<TDocument>(DocumentSerializerOptions)
                ?? throw new InvalidOperationException("Meilisearch returned a null document for a hit.");

            hits.Add(new SearchHit<TDocument>
            {
                Document = document,
                Rank = rank,
                Highlights = ExtractHighlights(raw, highlightPreTag),
            });
            rank++;
        }

        return hits;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ExtractHighlights(
        JsonElement raw, string? highlightPreTag)
    {
        if (highlightPreTag is null
            || !raw.TryGetProperty("_formatted", out var formatted)
            || formatted.ValueKind != JsonValueKind.Object)
        {
            return EmptyHighlights;
        }

        Dictionary<string, IReadOnlyList<string>>? result = null;
        foreach (var property in formatted.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = property.Value.GetString();
            if (string.IsNullOrEmpty(text) || !text.Contains(highlightPreTag, StringComparison.Ordinal))
            {
                continue;
            }

            result ??= [];
            result[property.Name] = new[] { text };
        }

        return result ?? EmptyHighlights;
    }

    private static IReadOnlyDictionary<string, FacetResult> MapFacets(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> facetDistribution,
        IReadOnlyDictionary<string, global::Meilisearch.FacetStat> facetStats,
        int maxFacetValues)
    {
        if (facetDistribution.Count == 0)
        {
            return EmptyFacets;
        }

        var result = new Dictionary<string, FacetResult>();
        foreach (var (field, valueCounts) in facetDistribution)
        {
            var values = valueCounts.Select(kv => new FacetValue(kv.Key, kv.Value)).ToArray();
            FacetNumericStats? stats = facetStats.TryGetValue(field, out var stat)
                ? new FacetNumericStats(stat.Min, stat.Max)
                : null;

            result[field] = new FacetResult
            {
                Field = field,
                Values = values,
                Stats = stats,
                Truncated = values.Length >= maxFacetValues,
            };
        }

        return result;
    }
}
