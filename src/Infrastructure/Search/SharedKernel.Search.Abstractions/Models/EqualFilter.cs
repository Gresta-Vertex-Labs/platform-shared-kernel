namespace SharedKernel.Search.Abstractions.Models;

/// <summary>An equality predicate: <see cref="Field"/> equals <see cref="Value"/>.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.Eq"/>.</remarks>
public sealed record EqualFilter : SearchFilter
{
    internal EqualFilter(string field, SearchValue value)
    {
        Field = field;
        Value = value;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the value <see cref="Field"/> must equal.</summary>
    public SearchValue Value { get; }
}
