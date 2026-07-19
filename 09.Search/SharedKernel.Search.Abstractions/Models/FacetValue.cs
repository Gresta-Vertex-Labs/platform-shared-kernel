namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A single facet value and its document count within a <see cref="FacetResult"/>.</summary>
public readonly record struct FacetValue
{
    /// <summary>Initializes a new <see cref="FacetValue"/>.</summary>
    public FacetValue(string value, long count)
    {
        Value = value;
        Count = count;
    }

    /// <summary>Gets the facet value.</summary>
    public string Value { get; }

    /// <summary>Gets the number of documents holding <see cref="Value"/> within the current result set.</summary>
    public long Count { get; }
}
