namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// The stable <see cref="SharedKernel.Primitives.Errors.Error.Code"/> values the paging requests and the cursor
/// codec report. Every one is an <see cref="SharedKernel.Primitives.Errors.ErrorType.Validation"/> error.
/// </summary>
public static class PaginationErrorCodes
{
    /// <summary>The page number is below 1 or too large to address: <c>pagination.page.out_of_range</c>.</summary>
    public const string PageOutOfRange = "pagination.page.out_of_range";

    /// <summary>The page size is below 1 or above the allowed maximum: <c>pagination.page_size.out_of_range</c>.</summary>
    public const string PageSizeOutOfRange = "pagination.page_size.out_of_range";

    /// <summary>The cursor page limit is below 1 or above the allowed maximum: <c>pagination.limit.out_of_range</c>.</summary>
    public const string LimitOutOfRange = "pagination.limit.out_of_range";

    /// <summary>The cursor is malformed, too long, or was not issued for this query: <c>pagination.cursor.invalid</c>.</summary>
    public const string CursorInvalid = "pagination.cursor.invalid";
}
