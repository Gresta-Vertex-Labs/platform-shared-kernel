namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>The result of a <see cref="StatsAggregation"/>.</summary>
public sealed record StatsResult : AggregationResult
{
    internal StatsResult(string name, long count, double min, double max, double average, double sum)
    {
        Name = name;
        Count = count;
        Min = min;
        Max = max;
        Average = average;
        Sum = sum;
    }

    /// <summary>Gets the aggregation's name.</summary>
    public string Name { get; }

    /// <summary>Gets the number of values counted.</summary>
    public long Count { get; }

    /// <summary>Gets the minimum value (0 when <see cref="Count"/> is 0).</summary>
    public double Min { get; }

    /// <summary>Gets the maximum value (0 when <see cref="Count"/> is 0).</summary>
    public double Max { get; }

    /// <summary>Gets the average value (0 when <see cref="Count"/> is 0).</summary>
    public double Average { get; }

    /// <summary>Gets the sum of all values.</summary>
    public double Sum { get; }
}
