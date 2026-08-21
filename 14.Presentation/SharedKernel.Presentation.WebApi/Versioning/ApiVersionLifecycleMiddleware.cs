using System.Globalization;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Versioning;

/// <summary>
/// Writes RFC 8594 <c>Sunset</c>, a boolean <c>Deprecation</c>, and (when applicable) a
/// <c>Link: rel="successor-version"</c> response header for the resolved API version of the current
/// request.
/// </summary>
/// <remarks>
/// <para>
/// Additive to, never a rework of, the existing <c>api-supported-versions</c>/
/// <c>api-deprecated-versions</c> headers <c>Asp.Versioning</c>'s own <c>ReportApiVersions</c> option
/// already writes — both header families may appear on the same response simultaneously.
/// </para>
/// <para>
/// Registered automatically via <see cref="ApiVersionLifecycleStartupFilter"/> — there is no
/// companion <c>Use...</c> method; this component self-inserts into the pipeline like every other
/// no-op-when-inapplicable filter/middleware in this domain. Headers are written from an
/// <see cref="HttpResponse.OnStarting(Func{Task})"/> callback (mirroring <c>CorrelationIdMiddleware</c>/
/// <c>SecurityHeadersMiddleware</c>) so the resolved API version and endpoint metadata — populated
/// during routing/endpoint selection — are reliably available by the time this callback runs,
/// regardless of where in the pipeline this middleware itself executes.
/// </para>
/// </remarks>
public sealed class ApiVersionLifecycleMiddleware
{
    private const string SunsetHeader = "Sunset";
    private const string DeprecationHeader = "Deprecation";
    private const string LinkHeader = "Link";
    private const string DeprecatedHeaderValue = "true";

    private readonly RequestDelegate _next;
    private readonly ApiVersionLifecycleOptions _options;

    /// <summary>
    /// Initialises a new <see cref="ApiVersionLifecycleMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="options">The declared per-version sunset/successor registry.</param>
    public ApiVersionLifecycleMiddleware(RequestDelegate next, ApiVersionLifecycleOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>
    /// Registers the lifecycle response headers to be written when the response starts, then
    /// invokes the rest of the pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request has been fully processed.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            ApplyLifecycleHeaders(context);
            return Task.CompletedTask;
        });

        return _next(context);
    }

    private void ApplyLifecycleHeaders(HttpContext context)
    {
        var requestedApiVersion = context.RequestedApiVersion;

        if (requestedApiVersion is null)
        {
            return;
        }

        _options.TryGetEntry(requestedApiVersion, out var entry);

        var headers = context.Response.Headers;

        if (entry.SunsetDate is { } sunsetDate && !headers.ContainsKey(SunsetHeader))
        {
            headers[SunsetHeader] = sunsetDate.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
        }

        if (IsDeprecated(context, requestedApiVersion) && !headers.ContainsKey(DeprecationHeader))
        {
            headers[DeprecationHeader] = DeprecatedHeaderValue;
        }

        // Link: rel="successor-version" only when a successor is declared ALONGSIDE a sunset date —
        // a successor with no sunset date produces no Link header (D-53).
        if (entry.SunsetDate is not null && entry.Successor is { } successor && !headers.ContainsKey(LinkHeader))
        {
            headers[LinkHeader] = $"<{successor}>; rel=\"successor-version\"";
        }
    }

    private static bool IsDeprecated(HttpContext context, ApiVersion requestedApiVersion)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<ApiVersionMetadata>();

        return metadata is not null
            && metadata.Map(ApiVersionMapping.Explicit).DeprecatedApiVersions.Contains(requestedApiVersion);
    }
}
