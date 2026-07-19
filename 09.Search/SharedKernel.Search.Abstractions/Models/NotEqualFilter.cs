namespace SharedKernel.Search.Abstractions.Models;

/// <summary>An inequality predicate: <see cref="Field"/> does not equal <see cref="Value"/>.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.Ne"/>.</remarks>
public sealed record NotEqualFilter : SearchFilter
{
    internal NotEqualFilter(string field, SearchValue value)
    {
        Field = field;
        Value = value;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the value <see cref="Field"/> must not equal.</summary>
    public SearchValue Value { get; }
}
