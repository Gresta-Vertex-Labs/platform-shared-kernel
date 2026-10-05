namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>A terms aggregation — a bucket per distinct field value, with an optional sub-aggregation set.</summary>
/// <remarks>Construct only via <see cref="AggregationRequest.Terms"/>.</remarks>
public sealed record TermsAggregation : AggregationRequest
{
    internal TermsAggregation(string name, string field, int size, IReadOnlyList<AggregationRequest> subAggregations)
    {
        Name = name;
        Field = field;
        Size = size;
        SubAggregations = subAggregations;
    }

    /// <summary>Gets the caller-supplied name this aggregation's result is keyed by.</summary>
    public string Name { get; }

    /// <summary>Gets the field to bucket by.</summary>
    public string Field { get; }

    /// <summary>Gets the maximum number of buckets to return.</summary>
    public int Size { get; }

    /// <summary>Gets the nested aggregations computed within each bucket.</summary>
    public IReadOnlyList<AggregationRequest> SubAggregations { get; }
}
