namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The result of a <see cref="DateHistogramAggregation"/>.</summary>
public sealed record DateHistogramResult : AggregationResult
{
    internal DateHistogramResult(string name, IReadOnlyList<DateHistogramBucket> buckets)
    {
        Name = name;
        Buckets = buckets;
    }

    /// <summary>Gets the aggregation's name.</summary>
    public string Name { get; }

    /// <summary>Gets the returned buckets, one per populated calendar interval.</summary>
    public IReadOnlyList<DateHistogramBucket> Buckets { get; }
}
