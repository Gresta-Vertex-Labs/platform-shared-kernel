namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A numeric-range aggregation — one bucket per caller-supplied <see cref="AggregationBucketRange"/>.</summary>
/// <remarks>Construct only via <see cref="AggregationRequest.Range"/>.</remarks>
public sealed record RangeAggregation : AggregationRequest
{
    internal RangeAggregation(string name, string field, IReadOnlyList<AggregationBucketRange> ranges)
    {
        Name = name;
        Field = field;
        Ranges = ranges;
    }

    /// <summary>Gets the caller-supplied name this aggregation's result is keyed by.</summary>
    public string Name { get; }

    /// <summary>Gets the numeric field to bucket by.</summary>
    public string Field { get; }

    /// <summary>Gets the requested bucket boundaries.</summary>
    public IReadOnlyList<AggregationBucketRange> Ranges { get; }
}
