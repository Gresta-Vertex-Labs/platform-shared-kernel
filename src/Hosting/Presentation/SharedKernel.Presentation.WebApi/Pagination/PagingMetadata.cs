namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Endpoint metadata that the endpoint takes paging query parameters, added by a <see cref="Paging"/> or
/// <see cref="CursorPaging"/> handler parameter: the requirements middleware validates them before the handler runs,
/// and the OpenAPI add-on documents them.
/// </summary>
internal sealed class PagingMetadata
{
    private PagingMetadata(bool isCursor)
    {
        IsCursor = isCursor;
    }

    /// <summary>Gets the metadata of a <see cref="Paging"/> parameter: <c>page</c> and <c>pageSize</c>.</summary>
    public static PagingMetadata Offset { get; } = new(isCursor: false);

    /// <summary>Gets the metadata of a <see cref="CursorPaging"/> parameter: <c>cursor</c> and <c>limit</c>.</summary>
    public static PagingMetadata Cursor { get; } = new(isCursor: true);

    /// <summary>Gets a value indicating whether the endpoint takes <c>cursor</c> and <c>limit</c> rather than <c>page</c> and <c>pageSize</c>.</summary>
    public bool IsCursor { get; }
}
