namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A set-membership predicate: <see cref="Field"/> is one of <see cref="Values"/>.</summary>
/// <remarks>Construct only via <see cref="SearchFilter.In"/>.</remarks>
public sealed record InFilter : SearchFilter
{
    internal InFilter(string field, IReadOnlyList<SearchValue> values)
    {
        Field = field;
        Values = values;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the candidate values <see cref="Field"/> may equal.</summary>
    public IReadOnlyList<SearchValue> Values { get; }
}
