namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A single bucket within a <see cref="DateHistogramResult"/>.</summary>
public sealed record DateHistogramBucket
{
    /// <summary>Gets the start of this bucket's calendar interval.</summary>
    public required DateTimeOffset Key { get; init; }

    /// <summary>Gets the number of documents in this bucket.</summary>
    public required long DocCount { get; init; }

    /// <summary>Gets this bucket's own nested aggregation results.</summary>
    public required AggregationResultSet SubAggregations { get; init; }
}
