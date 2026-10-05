namespace SharedKernel.Storage;

/// <summary>A request for one page of <see cref="IFileStorage.ListPageAsync"/>.</summary>
/// <example>
/// <code>
/// // One folder level at a time, 100 entries per page.
/// var first = new FileListRequest { Prefix = "2026/", Recursive = false, PageSize = 100 };
/// FileListPage page = (await store.ListPageAsync(first)).Value;
/// // page.Folders: "2026/09/", "2026/10/" ...; page.Items: objects directly under "2026/"
/// if (page.HasMore)
/// {
///     FileListRequest second = first with { ContinuationToken = page.ContinuationToken };
///     FileListPage next = (await store.ListPageAsync(second)).Value;
/// }
/// </code>
/// </example>
public sealed record FileListRequest
{
    /// <summary>The largest <see cref="PageSize"/> accepted: 1000, the S3 <c>ListObjectsV2</c> maximum.</summary>
    public const int MaxPageSize = 1000;

    /// <summary>
    /// Gets the key prefix, relative to the store (and tenant). Defaults to empty, which lists from the root. It
    /// is a plain string prefix: <c>2026/0</c> matches <c>2026/09/a</c> and <c>2026/0x</c>; for a folder, end it
    /// with <c>/</c>. Must be empty or a valid key, otherwise <see cref="StorageErrorCodes.InvalidKey"/>.
    /// </summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether objects in sub-folders are listed. Defaults to <see langword="true"/>. When
    /// <see langword="false"/>, keys are split on <c>/</c>: only objects directly under <see cref="Prefix"/> are in
    /// <see cref="FileListPage.Items"/>, and each sub-folder appears once in <see cref="FileListPage.Folders"/>.
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of objects and folders in the page, 1 to <see cref="MaxPageSize"/>. Defaults to
    /// <see cref="MaxPageSize"/>. Outside the range the request fails with
    /// <see cref="StorageErrorCodes.InvalidRequest"/>.
    /// </summary>
    public int PageSize { get; init; } = MaxPageSize;

    /// <summary>
    /// Gets the <see cref="FileListPage.ContinuationToken"/> of the previous page, or <see langword="null"/> for the
    /// first page. Only valid with the same <see cref="Prefix"/> and <see cref="Recursive"/> as that page.
    /// </summary>
    public string? ContinuationToken { get; init; }
}
