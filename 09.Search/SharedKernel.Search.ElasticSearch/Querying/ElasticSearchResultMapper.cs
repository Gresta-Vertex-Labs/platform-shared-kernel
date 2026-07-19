using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Querying;

/// <summary>Maps an ElasticSearch <see cref="SearchResponse{TDocument}"/> onto a neutral <see cref="SearchResults{TDocument}"/>.</summary>
internal static class ElasticSearchResultMapper
{
    private static readonly IReadOnlyDictionary<string, FacetResult> EmptyFacets = new Dictionary<string, FacetResult>();

    /// <summary>Maps <paramref name="response"/>. <see cref="SearchResponse{TDocument}.IsValidResponse"/> is checked first.</summary>
    public static Result<SearchResults<TDocument>> Map<TDocument>(
        SearchResponse<TDocument> response,
        int maxFacetValues,
        IReadOnlyList<string> facetFields,
        IReadOnlyList<string> facetStatsFields,
        int page,
        int pageSize,
        string providerName)
        where TDocument : class, ISearchDocument
    {
        if (!response.IsValidResponse)
        {
            return Result<SearchResults<TDocument>>.Failure(
                SearchErrors.EngineFault(providerName, "SearchAsync", response.DebugInformation));
        }

        var hits = MapHits(response.HitsMetadata.Hits);

        // response.IsValidResponse == true guarantees HitsMetadata.Total and Aggregations are populated.
        var (totalHits, accuracy) = response.HitsMetadata.Total!.Match(
            exact => (exact!.Value, exact.Relation == TotalHitsRelation.Eq ? TotalHitsAccuracy.Exact : TotalHitsAccuracy.LowerBound),
            longValue => (longValue, TotalHitsAccuracy.Exact));

        var facets = MapFacets(response.Aggregations!, facetFields, facetStatsFields, maxFacetValues);

        return Result<SearchResults<TDocument>>.Success(new SearchResults<TDocument>
        {
            Hits = hits,
            TotalHits = totalHits,
            Accuracy = accuracy,
            Page = page,
            PageSize = pageSize,
            Facets = facets,
            Duration = TimeSpan.FromMilliseconds(response.Took),
        });
    }

    private static IReadOnlyList<SearchHit<TDocument>> MapHits<TDocument>(IReadOnlyCollection<Hit<TDocument>> hits)
        where TDocument : class, ISearchDocument
    {
        var result = new List<SearchHit<TDocument>>(hits.Count);
        var rank = 0;
        foreach (var hit in hits)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> highlights = hit.Highlight is { Count: > 0 }
                ? hit.Highlight.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToArray())
                : new Dictionary<string, IReadOnlyList<string>>();

            result.Add(new SearchHit<TDocument>
            {
                Document = hit.Source ?? throw new InvalidOperationException("ElasticSearch returned a hit with no _source."),
                Rank = rank,
                Highlights = highlights,
            });
            rank++;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, FacetResult> MapFacets(
        AggregateDictionary aggregations,
        IReadOnlyList<string> facetFields,
        IReadOnlyList<string> facetStatsFields,
        int maxFacetValues)
    {
        if (facetFields.Count == 0 && facetStatsFields.Count == 0)
        {
            return EmptyFacets;
        }

        var result = new Dictionary<string, FacetResult>();

        foreach (var field in facetFields)
        {
            if (!aggregations.TryGetAggregate<StringTermsAggregate>(
                    ElasticSearchRequestTranslator.FacetAggregationName(field), out var terms))
            {
                continue;
            }

            var values = terms.Buckets.Select(b => new FacetValue(BucketKeyToString(b.Key), b.DocCount)).ToArray();
            result[field] = new FacetResult
            {
                Field = field,
                Values = values,
                Truncated = values.Length >= maxFacetValues,
            };
        }

        foreach (var field in facetStatsFields)
        {
            if (!aggregations.TryGetAggregate<StatsAggregate>(
                    ElasticSearchRequestTranslator.FacetStatsAggregationName(field), out var stats))
            {
                continue;
            }

            var statsValue = new FacetNumericStats(stats.Min ?? 0, stats.Max ?? 0);
            result[field] = result.TryGetValue(field, out var existing)
                ? existing with { Stats = statsValue }
                : new FacetResult { Field = field, Stats = statsValue };
        }

        return result;
    }

    private static string BucketKeyToString(FieldValue key) => key.TryGetString(out var value) ? value! : key.ToString();
}
