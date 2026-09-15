using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// A validated request for one page of an offset-paged result: a 1-based page number and a page size.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Bind the raw <c>page</c> and <c>pageSize</c> values from the query string as nullable integers and
/// pass them to <see cref="Create"/>. An absent value takes its default; an out-of-range value becomes a
/// validation error for the caller instead of an exception.
/// </para>
/// <para>
/// <b>Limits.</b> <see cref="MaxPageSize"/> matches the largest page <c>PagedSpecification</c> in
/// <c>SharedKernel.Domain</c> accepts, and <see cref="Offset"/> always fits in an <see cref="int"/>, so a valid
/// request can always be turned into a specification.
/// </para>
/// <para>
/// <b>Offset or cursor.</b> Offset paging supports "page 7 of 12" and a total count, but gets slower with depth and
/// shifts while rows are written. For deep, live or infinite-scroll results use <see cref="CursorPageRequest"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// app.MapGet("/orders", async (int? page, int? pageSize, ISender sender, CancellationToken ct) =&gt;
/// {
///     var request = PageRequest.Create(page, pageSize);
///     if (!request.IsValid)
///         return Results.ValidationProblem(...);
///
///     PagedList&lt;OrderSummary&gt; orders = await sender.Send(new ListOrders(request.Value), ct);
///     return Results.Ok(orders);
/// });
/// </code>
/// </example>
public sealed record PageRequest
{
    /// <summary>The page size used when none is supplied: 20.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The largest page size any request may ask for: 1000.</summary>
    public const int MaxPageSize = 1000;

    [JsonConstructor]
    internal PageRequest(int page, int pageSize)
    {
        if (FindErrors(page, pageSize, MaxPageSize).Count > 0)
            throw new JsonException($"Invalid page request: page {page}, page size {pageSize}.");

        Page = page;
        PageSize = pageSize;
    }

    /// <summary>Gets the first page with the default page size.</summary>
    [JsonIgnore]
    public static PageRequest First { get; } = new(1, DefaultPageSize);

    /// <summary>Gets the 1-based page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; }

    /// <summary>Gets the maximum number of items on the page, between 1 and <see cref="MaxPageSize"/>.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; }

    /// <summary>Gets the number of items before this page: <c>(Page - 1) * PageSize</c>.</summary>
    [JsonIgnore]
    public int Offset => (Page - 1) * PageSize;

    /// <summary>Validates a page number and page size and creates the request.</summary>
    /// <param name="page">The 1-based page number, or <see langword="null"/> for page 1.</param>
    /// <param name="pageSize">
    /// The page size, or <see langword="null"/> for <see cref="DefaultPageSize"/> (capped at
    /// <paramref name="maxPageSize"/>).
    /// </param>
    /// <param name="maxPageSize">
    /// The largest page size this endpoint allows. Must be between 1 and <see cref="MaxPageSize"/>.
    /// </param>
    /// <returns>
    /// A valid result with the request; otherwise a failure with a
    /// <see cref="PaginationErrorCodes.PageOutOfRange"/> error, a <see cref="PaginationErrorCodes.PageSizeOutOfRange"/>
    /// error, or both.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxPageSize"/> is below 1 or above <see cref="MaxPageSize"/>.
    /// </exception>
    public static ValidationResult<PageRequest> Create(int? page = null, int? pageSize = null, int maxPageSize = MaxPageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxPageSize, MaxPageSize);

        var resolvedPage = page ?? 1;
        var resolvedPageSize = pageSize ?? Math.Min(DefaultPageSize, maxPageSize);

        var errors = FindErrors(resolvedPage, resolvedPageSize, maxPageSize);
        return errors.Count == 0
            ? ValidationResult<PageRequest>.Success(new PageRequest(resolvedPage, resolvedPageSize))
            : ValidationResult<PageRequest>.Failure(errors);
    }

    private static List<Error> FindErrors(int page, int pageSize, int maxPageSize)
    {
        var errors = new List<Error>(2);

        var pageSizeInRange = pageSize >= 1 && pageSize <= maxPageSize;

        if (page < 1)
        {
            errors.Add(Error.Validation(PaginationErrorCodes.PageOutOfRange, "Page must be at least 1."));
        }
        else if (pageSizeInRange && ((long)page - 1) * pageSize > int.MaxValue)
        {
            errors.Add(Error.Validation(
                PaginationErrorCodes.PageOutOfRange,
                $"Page {page} is too far into the results for a page size of {pageSize}."));
        }

        if (!pageSizeInRange)
        {
            errors.Add(Error.Validation(
                PaginationErrorCodes.PageSizeOutOfRange,
                $"Page size must be between 1 and {maxPageSize}."));
        }

        return errors;
    }
}
