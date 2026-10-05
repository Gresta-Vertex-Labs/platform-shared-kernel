namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>
/// The closed, five-node abstract-syntax-tree base for an ElasticSearch aggregation request —
/// <see cref="IAnalyticsSearch{TDocument}.AggregateAsync"/>'s only input shape.
/// </summary>
/// <remarks>
/// <para>
/// Meilisearch offers <c>facetDistribution</c> (document counts) plus <c>facetStats</c> (numeric
/// min/max) and nothing else — no sum, no avg, no cardinality, no percentiles, no date histogram, no
/// nested or pipeline aggregations. The gap is absence, not degree, which is why this hierarchy is
/// declared here — in the ElasticSearch-exclusive package — rather than in
/// <c>SharedKernel.Search.Abstractions</c>: declaring it there would let a call site reference it
/// while referencing only Abstractions, so a provider swap would surface as a startup resolution
/// error. Declared here, the same call site takes a compile-time dependency on this package, so the
/// swap surfaces as a build error enumerating every non-portable site.
/// </para>
/// <para>Closed by construction: a <c>private protected</c> base constructor and <c>internal</c> subtype constructors.</para>
/// </remarks>
public abstract record AggregationRequest
{
    private protected AggregationRequest()
    {
    }

    /// <summary>Creates a terms aggregation, optionally with sub-aggregations.</summary>
    public static AggregationRequest Terms(
        string name, string field, int size, IReadOnlyList<AggregationRequest>? subAggregations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new TermsAggregation(name, field, size, subAggregations ?? []);
    }

    /// <summary>Creates a cardinality (approximate distinct count) aggregation.</summary>
    public static AggregationRequest Cardinality(string name, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new CardinalityAggregation(name, field);
    }

    /// <summary>Creates a numeric stats (count/min/max/average/sum) aggregation.</summary>
    public static AggregationRequest Stats(string name, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new StatsAggregation(name, field);
    }

    /// <summary>Creates a date-histogram aggregation, optionally with sub-aggregations.</summary>
    public static AggregationRequest DateHistogram(
        string name, string field, DateHistogramInterval interval, IReadOnlyList<AggregationRequest>? subAggregations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new DateHistogramAggregation(name, field, interval, subAggregations ?? []);
    }

    /// <summary>Creates a numeric-range aggregation.</summary>
    public static AggregationRequest Range(string name, string field, IReadOnlyList<AggregationBucketRange> ranges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(ranges);
        return new RangeAggregation(name, field, ranges);
    }
}
