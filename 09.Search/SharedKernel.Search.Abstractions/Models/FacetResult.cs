namespace SharedKernel.Search.Abstractions.Models;

/// <summary>The facet value distribution (and optional numeric stats) for one requested facet field.</summary>
public sealed record FacetResult
{
    /// <summary>Gets the facet field name.</summary>
    public required string Field { get; init; }

    /// <summary>Gets the value/count pairs for this facet, in engine-returned order.</summary>
    public IReadOnlyList<FacetValue> Values { get; init; } = [];

    /// <summary>
    /// Gets the numeric min/max statistics for this facet, or <see langword="null"/> when the field
    /// was not listed in <see cref="SearchRequest.NumericFacetStats"/> or is not numeric.
    /// </summary>
    public FacetNumericStats? Stats { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Values"/> was truncated at the provider's per-facet
    /// cap. Without this flag a caller cannot distinguish "exactly N facet values exist" from "there
    /// were more and they were cut", and would present a partial distribution as complete.
    /// </summary>
    public bool Truncated { get; init; }
}
