using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace SharedKernel.Presentation.WebApi.SecurityHeaders;

/// <summary>
/// Writes the configured security headers and the default <c>Cache-Control</c> when a response starts, keeping any
/// value an endpoint already set, and keeps the HSTS header on responses the exception handler writes.
/// </summary>
/// <remarks>
/// <para>
/// HSTS itself is ASP.NET Core's <c>UseHsts()</c>, which runs just before this middleware and sets the header
/// directly. The exception handler clears every response header before writing an error, so the value HSTS set is
/// remembered here and written again when the response starts, unless something set it since.
/// </para>
/// <para>
/// Registered before the exception handler, so error responses get every header too. The endpoint is known by the
/// time the response starts, so its own <c>Content-Security-Policy</c> can be honored.
/// </para>
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly WebApiSecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SharedKernelWebApiOptions> options)
    {
        _next = next;
        _options = options.Value.SecurityHeaders;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var state = new ResponseState(context, _options, context.Response.Headers.StrictTransportSecurity);

        context.Response.OnStarting(
            static state =>
            {
                ((ResponseState)state).Apply();
                return Task.CompletedTask;
            },
            state);

        return _next(context);
    }

    private sealed class ResponseState(HttpContext context, WebApiSecurityHeadersOptions options, StringValues strictTransportSecurity)
    {
        public void Apply()
        {
            var headers = context.Response.Headers;

            if (!StringValues.IsNullOrEmpty(strictTransportSecurity) && !headers.ContainsKey(HeaderNames.StrictTransportSecurity))
            {
                headers.StrictTransportSecurity = strictTransportSecurity;
            }

            SetIfAbsent(headers, HeaderNames.XContentTypeOptions, options.ContentTypeOptions);
            SetIfAbsent(headers, HeaderNames.XFrameOptions, options.FrameOptions);
            SetIfAbsent(headers, PresentationHeaderNames.ReferrerPolicy, options.ReferrerPolicy);
            SetIfAbsent(headers, PresentationHeaderNames.PermissionsPolicy, options.PermissionsPolicy);

            var endpointPolicy = RequestFacts.GetEndpoint(context)?.Metadata.GetMetadata<ContentSecurityPolicyMetadata>();
            SetIfAbsent(headers, HeaderNames.ContentSecurityPolicy, endpointPolicy is null ? options.ContentSecurityPolicy : endpointPolicy.Policy);

            // A response with an ETag is meant to be revalidated, one with Cache-Control has chosen its own caching.
            if (!headers.ContainsKey(HeaderNames.ETag))
            {
                SetIfAbsent(headers, HeaderNames.CacheControl, options.CacheControl);
            }
        }

        private static void SetIfAbsent(IHeaderDictionary headers, string name, string? value)
        {
            if (!string.IsNullOrEmpty(value) && !headers.ContainsKey(name))
            {
                headers[name] = value;
            }
        }
    }
}
