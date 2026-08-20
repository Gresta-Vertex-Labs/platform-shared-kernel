using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// Writes the platform's default HTTP response security headers
/// (<c>Strict-Transport-Security</c>, <c>X-Content-Type-Options</c>, <c>X-Frame-Options</c>,
/// <c>Referrer-Policy</c>, <c>Permissions-Policy</c>, and an optional <c>Content-Security-Policy</c>).
/// </summary>
/// <remarks>
/// Every header assignment is guarded by <see cref="IHeaderDictionary"/>'s inherited <c>ContainsKey</c> first — an
/// inner middleware/endpoint's more-specific header value always wins; this middleware never
/// overwrites an already-set header. Register via
/// <see cref="SecurityHeadersExtensions.UseSharedKernelSecurityHeaders"/> immediately after
/// <c>UseSharedKernelCorrelationId()</c> and before <c>UseExceptionHandler()</c>.
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private const string StrictTransportSecurityHeader = "Strict-Transport-Security";
    private const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    private const string FrameOptionsHeader = "X-Frame-Options";
    private const string ReferrerPolicyHeader = "Referrer-Policy";
    private const string PermissionsPolicyHeader = "Permissions-Policy";
    private const string ContentSecurityPolicyHeader = "Content-Security-Policy";

    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    /// <summary>
    /// Initialises a new <see cref="SecurityHeadersMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="options">The security header configuration to apply.</param>
    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options)
    {
        _next = next;
        _options = options;
    }

    /// <summary>
    /// Registers the platform's default security headers to be written when the response starts,
    /// then invokes the rest of the pipeline.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request has been fully processed.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        // Registered before next(): OnStarting fires even when downstream middleware
        // short-circuits the pipeline, mirroring CorrelationIdMiddleware's own discipline.
        context.Response.OnStarting(() =>
        {
            ApplyHeaders(context);
            return Task.CompletedTask;
        });

        return _next(context);
    }

    private void ApplyHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        if (_options.Hsts.Enabled && !headers.ContainsKey(StrictTransportSecurityHeader))
        {
            headers[StrictTransportSecurityHeader] = BuildHstsValue(_options.Hsts);
        }

        if (_options.ContentTypeOptions.Enabled && !headers.ContainsKey(ContentTypeOptionsHeader))
        {
            headers[ContentTypeOptionsHeader] = "nosniff";
        }

        if (_options.FrameOptions.Enabled && !headers.ContainsKey(FrameOptionsHeader))
        {
            headers[FrameOptionsHeader] = _options.FrameOptions.Value;
        }

        if (_options.ReferrerPolicy.Enabled && !headers.ContainsKey(ReferrerPolicyHeader))
        {
            headers[ReferrerPolicyHeader] = _options.ReferrerPolicy.Value;
        }

        if (_options.PermissionsPolicy.Enabled && !headers.ContainsKey(PermissionsPolicyHeader))
        {
            headers[PermissionsPolicyHeader] = _options.PermissionsPolicy.Value;
        }

        if (_options.ContentSecurityPolicy is { } csp && !headers.ContainsKey(ContentSecurityPolicyHeader))
        {
            headers[ContentSecurityPolicyHeader] = csp;
        }
    }

    private static string BuildHstsValue(HstsHeaderOptions hsts)
    {
        var value = $"max-age={(long)hsts.MaxAge.TotalSeconds}";

        if (hsts.IncludeSubDomains)
            value += "; includeSubDomains";

        if (hsts.Preload)
            value += "; preload";

        return value;
    }
}
