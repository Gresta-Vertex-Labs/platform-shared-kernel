namespace SharedKernel.Search.Abstractions.Models;

/// <summary>A single field-sort entry on a <see cref="SearchRequest"/>.</summary>
/// <remarks>
/// <para>
/// This deliberately duplicates a concept <c>03.Domain</c> also models — <c>09.Search</c> may
/// reference only <c>01.Core</c> and <c>04.Contracts</c>, so <c>03.Domain</c>'s ordering API is
/// behind a layering wall. Do not "fix" this by adding a <c>03.Domain</c> reference.
/// </para>
/// <para>
/// There is deliberately no <c>SortDirection.Relevance</c> and no score-based sort. An empty
/// <see cref="SearchRequest.Sort"/> collection <em>is</em> relevance order on both engines; naming it
/// as an explicit option would imply the two engines' relevance models are comparable across a
/// provider swap. They are not.
/// </para>
/// </remarks>
public readonly record struct SearchSort
{
    private SearchSort(string field, SortDirection direction)
    {
        Field = field;
        Direction = direction;
    }

    /// <summary>Gets the field name to sort on.</summary>
    public string Field { get; }

    /// <summary>Gets the sort direction.</summary>
    public SortDirection Direction { get; }

    /// <summary>Creates an ascending sort entry for <paramref name="field"/>.</summary>
    public static SearchSort Ascending(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new SearchSort(field, SortDirection.Ascending);
    }

    /// <summary>Creates a descending sort entry for <paramref name="field"/>.</summary>
    public static SearchSort Descending(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new SearchSort(field, SortDirection.Descending);
    }
}
