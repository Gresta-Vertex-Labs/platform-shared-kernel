using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Execution.Context;

namespace SharedKernel.Presentation;

/// <summary>
/// Facts about the current request that every presentation package — WebApi, gRPC, SignalR — must agree on.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Presentation.WebApi</c>'s own <c>RequestFacts</c> adds the HTTP-only facts (its options,
/// conditional requests, the problem instance) and forwards these (P-579).
/// </remarks>
internal static class RequestFacts
{
    private const string GrpcContentTypePrefix = "application/grpc";

    /// <summary>
    /// Returns the endpoint the request was routed to. While the exception handler runs, ASP.NET Core has cleared the
    /// endpoint, so the one recorded by <see cref="IExceptionHandlerFeature"/> is used instead.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The endpoint, or <see langword="null"/>.</returns>
    public static Endpoint? GetEndpoint(HttpContext httpContext) =>
        httpContext.GetEndpoint() ?? httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;

    /// <summary>Returns the display name of the endpoint for logs, never anything the caller sent.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The display name, or <c>(no endpoint)</c>.</returns>
    public static string GetEndpointDisplayName(HttpContext httpContext) =>
        GetEndpoint(httpContext)?.DisplayName ?? "(no endpoint)";

    /// <summary>Returns <see langword="true"/> for a gRPC call, whose errors travel as gRPC status, never as a body.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns><see langword="true"/> for a gRPC call.</returns>
    public static bool IsGrpcRequest(HttpContext httpContext) =>
        httpContext.Request.ContentType?.StartsWith(GrpcContentTypePrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Returns the culture client messages are written in: the request culture chosen by request localization when
    /// present, otherwise <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    /// <param name="httpContext">The current request, or <see langword="null"/>.</param>
    /// <returns>The culture.</returns>
    public static CultureInfo GetUICulture(HttpContext? httpContext) =>
        httpContext?.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// Returns <see langword="true"/> in the Development environment. Without a request, or without a registered
    /// <see cref="IHostEnvironment"/>, the answer is <see langword="false"/>: the safe assumption is production.
    /// </summary>
    /// <param name="httpContext">The current request, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> in Development.</returns>
    public static bool IsDevelopment(HttpContext? httpContext) =>
        httpContext?.RequestServices?.GetService<IHostEnvironment>()?.IsDevelopment() == true;

    /// <summary>
    /// Returns the correlation id of the call being handled: the one <c>SharedKernel.ServiceDefaults.Security</c>'s
    /// <c>UseSharedKernelRequestContext()</c> resolved and holds in the request's <see cref="RequestContextScope"/>.
    /// </summary>
    /// <param name="httpContext">
    /// The current request, or <see langword="null"/>. Its <see cref="IRequestContextAccessor"/> is asked when one is
    /// registered, the ambient <see cref="RequestContextScope.Current"/> otherwise; both read the same scope.
    /// </param>
    /// <returns>The correlation id, or <see langword="null"/> when no scope carrying one is open.</returns>
    public static string? GetCorrelationId(HttpContext? httpContext)
    {
        var accessor = httpContext?.RequestServices?.GetService<IRequestContextAccessor>();
        return (accessor?.Current ?? RequestContextScope.Current)?.CorrelationId;
    }
}
