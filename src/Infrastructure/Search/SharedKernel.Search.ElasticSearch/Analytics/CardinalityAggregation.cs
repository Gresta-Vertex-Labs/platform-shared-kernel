namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>An approximate distinct-count aggregation.</summary>
/// <remarks>Construct only via <see cref="AggregationRequest.Cardinality"/>.</remarks>
public sealed record CardinalityAggregation : AggregationRequest
{
    internal CardinalityAggregation(string name, string field)
    {
        Name = name;
        Field = field;
    }

    /// <summary>Gets the caller-supplied name this aggregation's result is keyed by.</summary>
    public string Name { get; }

    /// <summary>Gets the field to count distinct values of.</summary>
    public string Field { get; }
}
