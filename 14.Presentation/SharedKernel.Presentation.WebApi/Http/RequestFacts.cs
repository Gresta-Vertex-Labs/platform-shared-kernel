using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Facts about the current request that several parts of this package need to agree on.</summary>
/// <remarks>
/// The facts gRPC and SignalR need too — the endpoint, gRPC detection, the UI culture, the environment, the correlation
/// id — are <c>SharedKernel.Presentation.Core</c>'s, forwarded here (P-579).
/// </remarks>
internal static class RequestFacts
{
    private static readonly SharedKernelWebApiOptions DefaultOptions = new();

    /// <summary>
    /// Returns the endpoint the request was routed to, or the one recorded by <see cref="IExceptionHandlerFeature"/>
    /// while the exception handler runs.
    /// </summary>
    public static Endpoint? GetEndpoint(HttpContext httpContext) => Presentation.RequestFacts.GetEndpoint(httpContext);

    /// <summary>Returns the registered <see cref="SharedKernelWebApiOptions"/>, or the defaults when none are registered.</summary>
    public static SharedKernelWebApiOptions GetOptions(HttpContext? httpContext) =>
        httpContext?.RequestServices?.GetService<IOptions<SharedKernelWebApiOptions>>()?.Value ?? DefaultOptions;

    /// <summary>Returns <see langword="true"/> for a gRPC call, whose errors travel as gRPC status, never as a body.</summary>
    public static bool IsGrpcRequest(HttpContext httpContext) => Presentation.RequestFacts.IsGrpcRequest(httpContext);

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

    /// <summary>Returns the culture client messages are written in.</summary>
    public static CultureInfo GetUICulture(HttpContext? httpContext) => Presentation.RequestFacts.GetUICulture(httpContext);

    /// <summary>Returns <see langword="true"/> in the Development environment; <see langword="false"/> when unknown.</summary>
    public static bool IsDevelopment(HttpContext? httpContext) => Presentation.RequestFacts.IsDevelopment(httpContext);

    /// <summary>Returns the correlation id held by the request's <c>RequestContextScope</c>, or <see langword="null"/>.</summary>
    public static string? GetCorrelationId(HttpContext? httpContext) => Presentation.RequestFacts.GetCorrelationId(httpContext);

    /// <summary>Returns the request path, as it was before the exception handler ran.</summary>
    public static string GetInstance(HttpContext httpContext)
    {
        var path = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path.Value;
        var instance = httpContext.Request.PathBase.Value + path;
        return string.IsNullOrEmpty(instance) ? "/" : instance;
    }

    /// <summary>Returns the display name of the endpoint for logs, never anything the caller sent.</summary>
    public static string GetEndpointDisplayName(HttpContext httpContext) =>
        Presentation.RequestFacts.GetEndpointDisplayName(httpContext);
}
