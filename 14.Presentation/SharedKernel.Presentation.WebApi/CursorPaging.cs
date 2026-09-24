using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The <c>cursor</c> and <c>limit</c> query parameters of a request, as a minimal-API handler parameter, bound into a
/// validated <see cref="CursorPageRequest"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>app.MapGet("/invoices/feed", (CursorPaging paging, ISender sender, CancellationToken ct) =&gt; sender.Send(new
/// ListInvoiceFeed(paging.Request), ct).ToOk())</c>. A parameter that is absent (or blank) takes the default of
/// <see cref="CursorPageRequest.Create"/>: the first page of <see cref="CursorPageRequest.DefaultLimit"/> items.
/// </para>
/// <para>
/// Invalid input never reaches the handler: <c>UseSharedKernelWebApi()</c> answers it with 400 and the platform's
/// validation problem — <c>errorCode</c> <c>validation.failed</c>, and <c>errors</c>/<c>errorCodes</c> keyed by
/// <c>cursor</c> and <c>limit</c>. A cursor longer than <see cref="PageCursor.MaxLength"/> or made of white space
/// carries 04.Contracts' <c>pagination.cursor.invalid</c>, a limit out of range <c>pagination.limit.out_of_range</c>,
/// and a limit that is not one whole number or a parameter sent twice <c>validation.invalid_format</c>. The OpenAPI
/// add-on documents both query parameters and the 400.
/// </para>
/// <para>
/// The cursor is still opaque here: decode it with <see cref="PageCursor.Decode{TKey, TId}"/> where the key types are
/// known (06.Persistence's <c>ListKeysetAsync</c> does), and treat a decoding failure as a validation error too.
/// Minimal APIs only. In a unit test, construct one directly: <c>new CursorPaging(CursorPageRequest.First)</c>.
/// </para>
/// </remarks>
public sealed record CursorPaging : IEndpointParameterMetadataProvider
{
    /// <summary>Initializes a new instance of the <see cref="CursorPaging"/> class.</summary>
    /// <param name="request">The validated cursor page request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public CursorPaging(CursorPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Request = request;
    }

    /// <summary>Gets the validated cursor page request.</summary>
    public CursorPageRequest Request { get; }

    /// <summary>Binds the parameter from the request's <c>cursor</c> and <c>limit</c> query parameters.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>The paging of the request; the first page when it sends neither parameter.</returns>
    /// <exception cref="BadHttpRequestException">
    /// A parameter is invalid (status 400). This happens only when <c>UseSharedKernelWebApi()</c> is not in the
    /// pipeline to refuse the request first with the problem that names each parameter.
    /// </exception>
    public static ValueTask<CursorPaging?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = PagingQuery.ReadCursor(context.Request.Query);
        return request.IsValid
            ? ValueTask.FromResult<CursorPaging?>(new CursorPaging(request.Value))
            : throw new BadHttpRequestException(request.Errors[0].Message, StatusCodes.Status400BadRequest);
    }

    /// <inheritdoc />
    static void IEndpointParameterMetadataProvider.PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(PagingMetadata.Cursor);
    }
}
