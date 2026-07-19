namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A single bucket within a <see cref="TermsResult"/>.</summary>
public sealed record TermsBucket
{
    /// <summary>Gets the distinct field value this bucket represents.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the number of documents in this bucket.</summary>
    public required long DocCount { get; init; }

    /// <summary>Gets this bucket's own nested aggregation results.</summary>
    public required AggregationResultSet SubAggregations { get; init; }
}
