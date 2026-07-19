namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A numeric stats (count/min/max/average/sum) aggregation.</summary>
/// <remarks>Construct only via <see cref="AggregationRequest.Stats"/>.</remarks>
public sealed record StatsAggregation : AggregationRequest
{
    internal StatsAggregation(string name, string field)
    {
        Name = name;
        Field = field;
    }

    /// <summary>Gets the caller-supplied name this aggregation's result is keyed by.</summary>
    public string Name { get; }

    /// <summary>Gets the numeric field to compute statistics over.</summary>
    public string Field { get; }
}
