namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A single numeric bucket boundary requested on a <see cref="RangeAggregation"/>.</summary>
public readonly record struct AggregationBucketRange
{
    /// <summary>Initializes a new <see cref="AggregationBucketRange"/>.</summary>
    public AggregationBucketRange(string key, double? from, double? to)
    {
        Key = key;
        From = from;
        To = to;
    }

    /// <summary>Gets the caller-supplied label for this bucket.</summary>
    public string Key { get; }

    /// <summary>Gets the inclusive lower bound, or <see langword="null"/> for an open lower end.</summary>
    public double? From { get; }

    /// <summary>Gets the exclusive upper bound, or <see langword="null"/> for an open upper end.</summary>
    public double? To { get; }
}
