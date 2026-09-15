using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// A validated request for one page of a cursor-paged result: the opaque cursor from the previous page, or none for
/// the first page, and the maximum number of items to return.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Bind the raw <c>cursor</c> and <c>limit</c> values from the query string and pass them to
/// <see cref="Create"/>. An empty cursor means the first page. The request checks only the cursor's length;
/// decode it with <see cref="PageCursor.Decode{TKey, TId}"/> to get the keyset position, and treat a decoding
/// failure as a validation error too.
/// </para>
/// <para>
/// <b>Limits.</b> <see cref="MaxLimit"/> matches <see cref="PageRequest.MaxPageSize"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var request = CursorPageRequest.Create(cursor, limit);
/// if (!request.IsValid)
///     return Results.ValidationProblem(...);
///
/// DateTimeOffset? afterCreatedOn = null;
/// Guid? afterId = null;
/// if (request.Value.Cursor is { } token)
/// {
///     var position = PageCursor.Decode&lt;DateTimeOffset, Guid&gt;(token);
///     if (position.IsFailure)
///         return Results.ValidationProblem(...);
///     (afterCreatedOn, afterId) = (position.Value.Key, position.Value.Id);
/// }
/// </code>
/// </example>
public sealed record CursorPageRequest
{
    /// <summary>The limit used when none is supplied: 20.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest limit any request may ask for: 1000.</summary>
    public const int MaxLimit = PageRequest.MaxPageSize;

    [JsonConstructor]
    internal CursorPageRequest(string? cursor, int limit)
    {
        if (FindErrors(cursor, limit, MaxLimit).Count > 0)
            throw new JsonException($"Invalid cursor page request: limit {limit}.");

        Cursor = string.IsNullOrEmpty(cursor) ? null : cursor;
        Limit = limit;
    }

    /// <summary>Gets the first page with the default limit.</summary>
    [JsonIgnore]
    public static CursorPageRequest First { get; } = new(null, DefaultLimit);

    /// <summary>
    /// Gets the opaque cursor returned as <see cref="CursorPagedList{T}.NextCursor"/> by the previous page, or
    /// <see langword="null"/> for the first page.
    /// </summary>
    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; }

    /// <summary>Gets the maximum number of items to return, between 1 and <see cref="MaxLimit"/>.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; }

    /// <summary>Gets a value indicating whether this requests the first page, which has no cursor.</summary>
    [JsonIgnore]
    public bool IsFirstPage => Cursor is null;

    /// <summary>Validates a cursor and limit and creates the request.</summary>
    /// <param name="cursor">
    /// The cursor from the previous page, or <see langword="null"/> or empty for the first page. At most
    /// <see cref="PageCursor.MaxLength"/> characters.
    /// </param>
    /// <param name="limit">
    /// The maximum number of items, or <see langword="null"/> for <see cref="DefaultLimit"/> (capped at
    /// <paramref name="maxLimit"/>).
    /// </param>
    /// <param name="maxLimit">The largest limit this endpoint allows. Must be between 1 and <see cref="MaxLimit"/>.</param>
    /// <returns>
    /// A valid result with the request; otherwise a failure with a <see cref="PaginationErrorCodes.CursorInvalid"/>
    /// error, a <see cref="PaginationErrorCodes.LimitOutOfRange"/> error, or both.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxLimit"/> is below 1 or above <see cref="MaxLimit"/>.
    /// </exception>
    public static ValidationResult<CursorPageRequest> Create(string? cursor = null, int? limit = null, int maxLimit = MaxLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLimit, MaxLimit);

        var resolvedLimit = limit ?? Math.Min(DefaultLimit, maxLimit);

        var errors = FindErrors(cursor, resolvedLimit, maxLimit);
        return errors.Count == 0
            ? ValidationResult<CursorPageRequest>.Success(new CursorPageRequest(cursor, resolvedLimit))
            : ValidationResult<CursorPageRequest>.Failure(errors);
    }

    private static List<Error> FindErrors(string? cursor, int limit, int maxLimit)
    {
        var errors = new List<Error>(2);

        if (cursor is { Length: > 0 } && (cursor.Length > PageCursor.MaxLength || string.IsNullOrWhiteSpace(cursor)))
            errors.Add(PageCursor.InvalidCursorError);

        if (limit < 1 || limit > maxLimit)
        {
            errors.Add(Error.Validation(
                PaginationErrorCodes.LimitOutOfRange,
                $"Limit must be between 1 and {maxLimit}."));
        }

        return errors;
    }
}
