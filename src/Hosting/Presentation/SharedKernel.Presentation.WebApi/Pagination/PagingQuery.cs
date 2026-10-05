using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Reads the paging query parameters of a request into 04.Contracts' validated requests, for the
/// <see cref="Paging"/> and <see cref="CursorPaging"/> parameters and the requirements middleware alike.
/// </summary>
/// <remarks>
/// A parameter that is absent or blank takes the default <see cref="PageRequest.Create"/> or
/// <see cref="CursorPageRequest.Create"/> applies. A value that is not one integer — text, a decimal, a number beyond
/// <see cref="int"/>, or the parameter repeated — is <c>validation.invalid_format</c>, the code the platform gives any
/// value it cannot read; a readable value outside its range carries the <c>pagination.*</c> code of 04.Contracts. Every
/// failure is keyed by the query parameter's name, so the problem response lists it under <c>errors</c> and
/// <c>errorCodes</c> as <c>page</c>, <c>pageSize</c>, <c>cursor</c> or <c>limit</c>.
/// </remarks>
internal static class PagingQuery
{
    /// <summary>The 1-based page number.</summary>
    public const string Page = "page";

    /// <summary>The number of items per page.</summary>
    public const string PageSize = "pageSize";

    /// <summary>The opaque cursor of the previous page.</summary>
    public const string Cursor = "cursor";

    /// <summary>The maximum number of items of a cursor page.</summary>
    public const string Limit = "limit";

    /// <summary>The message of a paging value that is not one integer.</summary>
    public const string InvalidNumberMessage = "The value must be a whole number.";

    /// <summary>The message of a cursor sent more than once.</summary>
    public const string RepeatedCursorMessage = "The cursor must be sent once.";

    /// <summary>Reads <c>page</c> and <c>pageSize</c>.</summary>
    public static ValidationResult<PageRequest> ReadPage(IQueryCollection query)
    {
        List<Error> errors = [];
        var page = ReadNumber(query, Page, errors);
        var pageSize = ReadNumber(query, PageSize, errors);

        var request = PageRequest.Create(page, pageSize);
        return Complete(request, errors, [Page, PageSize]);
    }

    /// <summary>Reads <c>cursor</c> and <c>limit</c>.</summary>
    public static ValidationResult<CursorPageRequest> ReadCursor(IQueryCollection query)
    {
        List<Error> errors = [];
        var cursor = ReadText(query, Cursor, errors);
        var limit = ReadNumber(query, Limit, errors);

        var request = CursorPageRequest.Create(cursor, limit);
        return Complete(request, errors, [Cursor, Limit]);
    }

    private static ValidationResult<T> Complete<T>(ValidationResult<T> request, List<Error> readErrors, string[] order)
    {
        if (request.IsValid && readErrors.Count == 0)
        {
            return request;
        }

        // A parameter that could not be read was replaced by its default, so the contract reported nothing for it;
        // what it did report is keyed here. The errors follow the order of the query parameters.
        var all = readErrors.Concat(request.Errors.Select(error => ForField(error, FieldOf(error.Code))));
        return ValidationResult<T>.Failure([.. all.OrderBy(error => Array.IndexOf(order, PathOf(error)))]);
    }

    private static int? ReadNumber(IQueryCollection query, string name, List<Error> errors)
    {
        var values = query[name];
        if (IsAbsent(values))
        {
            return null;
        }

        if (values.Count == 1
            && int.TryParse(values[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        errors.Add(ForField(Error.Validation(ErrorCodes.Validation.InvalidFormat, InvalidNumberMessage), name));
        return null;
    }

    private static string? ReadText(IQueryCollection query, string name, List<Error> errors)
    {
        var values = query[name];
        if (IsAbsent(values))
        {
            return null;
        }

        if (values.Count == 1)
        {
            return values[0];
        }

        errors.Add(ForField(Error.Validation(ErrorCodes.Validation.InvalidFormat, RepeatedCursorMessage), name));
        return null;
    }

    // A blank value is a missing one: "?page=" asks for the default, as a blank header is a missing one.
    private static bool IsAbsent(StringValues values) => values.All(string.IsNullOrEmpty);

    private static string FieldOf(string code) => code switch
    {
        PaginationErrorCodes.PageOutOfRange => Page,
        PaginationErrorCodes.PageSizeOutOfRange => PageSize,
        PaginationErrorCodes.CursorInvalid => Cursor,
        PaginationErrorCodes.LimitOutOfRange => Limit,
        _ => string.Empty,
    };

    private static string? PathOf(Error error) =>
        error.MessageArguments.TryGetValue(ErrorArgumentNames.PropertyPath, out var path) ? path as string : null;

    private static Error ForField(Error error, string field)
    {
        if (field.Length == 0)
        {
            return error;
        }

        var arguments = new Dictionary<string, object?>(error.MessageArguments, StringComparer.Ordinal)
        {
            [ErrorArgumentNames.PropertyPath] = field,
        };

        return error with { MessageArguments = arguments };
    }
}
