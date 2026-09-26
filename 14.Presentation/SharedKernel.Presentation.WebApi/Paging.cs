using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The <c>page</c> and <c>pageSize</c> query parameters of a request, as a minimal-API handler parameter, bound into a
/// validated <see cref="PageRequest"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>app.MapGet("/invoices", (Paging paging, ISender sender, CancellationToken ct) =&gt; sender.Send(new
/// ListInvoices(paging.Request), ct).ToOk())</c>. A parameter that is absent (or blank) takes the default of
/// <see cref="PageRequest.Create"/>: page 1 of <see cref="PageRequest.DefaultPageSize"/> items.
/// </para>
/// <para>
/// Invalid input never reaches the handler: <c>UseSharedKernelWebApi()</c> answers it with 400 and the platform's
/// validation problem — <c>errorCode</c> <c>validation.failed</c>, and <c>errors</c>/<c>errorCodes</c> keyed by
/// <c>page</c> and <c>pageSize</c>. A value out of range carries 04.Contracts' <c>pagination.page.out_of_range</c> or
/// <c>pagination.page_size.out_of_range</c>; a value that is not one whole number (text, a decimal, a number too large
/// for an <see cref="int"/>, the parameter repeated) carries <c>validation.invalid_format</c>. The OpenAPI add-on
/// documents both query parameters and the 400.
/// </para>
/// <para>
/// Minimal APIs only. An MVC action binds the two values itself and calls <see cref="PageRequest.Create"/>. In a unit
/// test, construct one directly: <c>new Paging(PageRequest.First)</c>.
/// </para>
/// </remarks>
public sealed record Paging : IEndpointParameterMetadataProvider
{
    /// <summary>Initializes a new instance of the <see cref="Paging"/> class.</summary>
    /// <param name="request">The validated page request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public Paging(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Request = request;
    }

    /// <summary>Gets the validated page request.</summary>
    public PageRequest Request { get; }

    /// <summary>Binds the parameter from the request's <c>page</c> and <c>pageSize</c> query parameters.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>The paging of the request; the defaults when it sends neither parameter.</returns>
    /// <exception cref="BadHttpRequestException">
    /// A parameter is invalid (status 400). This happens only when <c>UseSharedKernelWebApi()</c> is not in the
    /// pipeline to refuse the request first with the problem that names each parameter.
    /// </exception>
    public static ValueTask<Paging?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = PagingQuery.ReadPage(context.Request.Query);
        return request.IsValid
            ? ValueTask.FromResult<Paging?>(new Paging(request.Value))
            : throw new BadHttpRequestException(request.Errors[0].Message, StatusCodes.Status400BadRequest);
    }

    /// <inheritdoc />
    static void IEndpointParameterMetadataProvider.PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(PagingMetadata.Offset);
    }
}
