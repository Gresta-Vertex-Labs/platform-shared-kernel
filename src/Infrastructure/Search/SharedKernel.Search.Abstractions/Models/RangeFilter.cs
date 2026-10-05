namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// A range predicate: <see cref="Field"/> falls between <see cref="From"/> and <see cref="To"/>
/// (either bound may be <see langword="null"/> for an open end).
/// </summary>
/// <remarks>
/// Construct only via <see cref="SearchFilter.Between"/>, which rejects
/// <see cref="SearchValueKind.String"/> and <see cref="SearchValueKind.Boolean"/> bounds.
/// </remarks>
public sealed record RangeFilter : SearchFilter
{
    internal RangeFilter(string field, SearchValue? from, bool fromInclusive, SearchValue? to, bool toInclusive)
    {
        Field = field;
        From = from;
        FromInclusive = fromInclusive;
        To = to;
        ToInclusive = toInclusive;
    }

    /// <summary>Gets the field name this predicate applies to.</summary>
    public string Field { get; }

    /// <summary>Gets the lower bound, or <see langword="null"/> for an open lower end.</summary>
    public SearchValue? From { get; }

    /// <summary>Gets a value indicating whether <see cref="From"/> is inclusive.</summary>
    public bool FromInclusive { get; }

    /// <summary>Gets the upper bound, or <see langword="null"/> for an open upper end.</summary>
    public SearchValue? To { get; }

    /// <summary>Gets a value indicating whether <see cref="To"/> is inclusive.</summary>
    public bool ToInclusive { get; }
}
