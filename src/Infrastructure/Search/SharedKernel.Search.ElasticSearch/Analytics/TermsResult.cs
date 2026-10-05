namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The result of a <see cref="TermsAggregation"/>.</summary>
public sealed record TermsResult : AggregationResult
{
    internal TermsResult(string name, IReadOnlyList<TermsBucket> buckets, long otherDocCount)
    {
        Name = name;
        Buckets = buckets;
        OtherDocCount = otherDocCount;
    }

    /// <summary>Gets the aggregation's name.</summary>
    public string Name { get; }

    /// <summary>Gets the returned buckets, one per distinct field value up to the requested size.</summary>
    public IReadOnlyList<TermsBucket> Buckets { get; }

    /// <summary>Gets the number of documents belonging to buckets not returned (beyond the requested size).</summary>
    public long OtherDocCount { get; }
}
