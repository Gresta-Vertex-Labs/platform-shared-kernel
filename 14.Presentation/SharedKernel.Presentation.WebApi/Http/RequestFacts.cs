using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SharedKernel.Presentation.WebApi.Options;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>Facts about the current request that several parts of this package need to agree on.</summary>
internal static class RequestFacts
{
    private const string GrpcContentTypePrefix = "application/grpc";

    private static readonly SharedKernelWebApiOptions DefaultOptions = new();

    /// <summary>
    /// Returns the endpoint the request was routed to. While the exception handler runs, ASP.NET Core has cleared the
    /// endpoint, so the one recorded by <see cref="IExceptionHandlerFeature"/> is used instead.
    /// </summary>
    public static Endpoint? GetEndpoint(HttpContext httpContext) =>
        httpContext.GetEndpoint() ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;

    /// <summary>Returns the registered <see cref="SharedKernelWebApiOptions"/>, or the defaults when none are registered.</summary>
    public static SharedKernelWebApiOptions GetOptions(HttpContext? httpContext) =>
        httpContext?.RequestServices?.GetService<IOptions<SharedKernelWebApiOptions>>()?.Value ?? DefaultOptions;

    /// <summary>Returns <see langword="true"/> for a gRPC call, whose errors travel as gRPC status, never as a body.</summary>
    public static bool IsGrpcRequest(HttpContext httpContext) =>
        httpContext.Request.ContentType?.StartsWith(GrpcContentTypePrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Returns <see langword="true"/> when the request carries a non-empty <c>If-Match</c> or <c>If-None-Match</c>.</summary>
    public static bool IsConditionalRequest(HttpRequest request) =>
        HasValue(request.Headers.IfMatch) || HasValue(request.Headers.IfNoneMatch);

    /// <summary>Returns <see langword="true"/> when at least one value of a header is not white space.</summary>
    public static bool HasValue(StringValues values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the culture client messages are written in: the request culture chosen by request localization when
    /// present, otherwise <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public static CultureInfo GetUICulture(HttpContext? httpContext) =>
        httpContext?.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// Returns <see langword="true"/> in the Development environment. Without a request, or without a registered
    /// <see cref="IHostEnvironment"/>, the answer is <see langword="false"/>: the safe assumption is production.
    /// </summary>
    public static bool IsDevelopment(HttpContext? httpContext) =>
        httpContext?.RequestServices?.GetService<IHostEnvironment>()?.IsDevelopment() == true;

    /// <summary>Returns the request path, as it was before the exception handler ran.</summary>
    public static string GetInstance(HttpContext httpContext)
    {
        var path = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path.Value;
        var instance = httpContext.Request.PathBase.Value + path;
        return string.IsNullOrEmpty(instance) ? "/" : instance;
    }

    /// <summary>Returns the display name of the endpoint for logs, never anything the caller sent.</summary>
    public static string GetEndpointDisplayName(HttpContext httpContext) =>
        GetEndpoint(httpContext)?.DisplayName ?? "(no endpoint)";
}
