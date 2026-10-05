namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A date-histogram aggregation — a bucket per calendar interval, with an optional sub-aggregation set.</summary>
/// <remarks>Construct only via <see cref="AggregationRequest.DateHistogram"/>.</remarks>
public sealed record DateHistogramAggregation : AggregationRequest
{
    internal DateHistogramAggregation(
        string name, string field, DateHistogramInterval interval, IReadOnlyList<AggregationRequest> subAggregations)
    {
        Name = name;
        Field = field;
        Interval = interval;
        SubAggregations = subAggregations;
    }

    /// <summary>Gets the caller-supplied name this aggregation's result is keyed by.</summary>
    public string Name { get; }

    /// <summary>Gets the date field to bucket by.</summary>
    public string Field { get; }

    /// <summary>Gets the calendar interval each bucket spans.</summary>
    public DateHistogramInterval Interval { get; }

    /// <summary>Gets the nested aggregations computed within each bucket.</summary>
    public IReadOnlyList<AggregationRequest> SubAggregations { get; }
}
