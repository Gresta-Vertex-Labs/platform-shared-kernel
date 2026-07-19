namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A single bucket within a <see cref="RangeResult"/>.</summary>
public readonly record struct RangeBucket
{
    /// <summary>Initializes a new <see cref="RangeBucket"/>.</summary>
    public RangeBucket(string key, long docCount)
    {
        Key = key;
        DocCount = docCount;
    }

    /// <summary>Gets the caller-supplied label of the matching <see cref="AggregationBucketRange"/>.</summary>
    public string Key { get; }

    /// <summary>Gets the number of documents in this bucket.</summary>
    public long DocCount { get; }
}
