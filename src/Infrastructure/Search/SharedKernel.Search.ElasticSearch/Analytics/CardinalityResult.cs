namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The result of a <see cref="CardinalityAggregation"/>.</summary>
public sealed record CardinalityResult : AggregationResult
{
    internal CardinalityResult(string name, long value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Gets the aggregation's name.</summary>
    public string Name { get; }

    /// <summary>Gets the approximate distinct count.</summary>
    public long Value { get; }
}
