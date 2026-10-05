namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The minimum and maximum values of a numeric facet field within the current result set — the
/// entire portable numeric-aggregation surface on the neutral read path.
/// </summary>
public readonly record struct FacetNumericStats
{
    /// <summary>Initializes a new <see cref="FacetNumericStats"/>.</summary>
    public FacetNumericStats(double min, double max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Gets the minimum value.</summary>
    public double Min { get; }

    /// <summary>Gets the maximum value.</summary>
    public double Max { get; }
}
