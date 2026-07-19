namespace SharedKernel.Search.ElasticSearch.Analytics;

/// <summary>
/// The closed, five-node result counterpart to <see cref="AggregationRequest"/>. Read back through
/// <see cref="AggregationResultSet"/>'s name-keyed, type-specific <c>TryGet*</c> accessors — never
/// constructed by callers.
/// </summary>
public abstract record AggregationResult
{
    private protected AggregationResult()
    {
    }
}
