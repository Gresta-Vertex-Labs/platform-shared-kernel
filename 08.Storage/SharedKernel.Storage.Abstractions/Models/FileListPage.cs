namespace SharedKernel.Storage;

/// <summary>
/// One page of <see cref="IFileStorage.ListPageAsync"/>: objects, sub-folders and the token for the next page.
/// </summary>
/// <remarks>
/// <see cref="Items"/> and <see cref="Folders"/> together hold at most <see cref="FileListRequest.PageSize"/>
/// entries. A page can be empty and still have <see cref="HasMore"/> set; keep paging until it is
/// <see langword="false"/>.
/// </remarks>
public sealed record FileListPage
{
    /// <summary>Gets the objects in this page in key order, with keys relative to the store (and tenant).</summary>
    public required IReadOnlyList<FileListItem> Items { get; init; }

    /// <summary>
    /// Gets the sub-folders directly under <see cref="FileListRequest.Prefix"/> for a non-recursive listing: the
    /// full relative path of each, ending with <c>/</c> (prefix <c>2026/</c> gives <c>2026/09/</c>). Pass one as
    /// the next <see cref="FileListRequest.Prefix"/> to descend. Empty for a recursive listing.
    /// </summary>
    public required IReadOnlyList<string> Folders { get; init; }

    /// <summary>
    /// Gets the opaque token for the next page, or <see langword="null"/> when this is the last page. Pass it as
    /// <see cref="FileListRequest.ContinuationToken"/> with the same prefix and recursion; it is not a key.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Gets a value indicating whether more pages follow (<see cref="ContinuationToken"/> is not
    /// <see langword="null"/>).
    /// </summary>
    public bool HasMore => ContinuationToken is not null;
}
