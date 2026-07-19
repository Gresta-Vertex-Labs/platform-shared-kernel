namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The result of a <see cref="RangeAggregation"/>.</summary>
public sealed record RangeResult : AggregationResult
{
    internal RangeResult(string name, IReadOnlyList<RangeBucket> buckets)
    {
        Name = name;
        Buckets = buckets;
    }

    /// <summary>Gets the aggregation's name.</summary>
    public string Name { get; }

    /// <summary>Gets the returned buckets, one per requested <see cref="AggregationBucketRange"/>.</summary>
    public IReadOnlyList<RangeBucket> Buckets { get; }
}
