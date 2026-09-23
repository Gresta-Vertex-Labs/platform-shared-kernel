using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Options;

namespace SharedKernel.Presentation.WebApi.SecurityHeaders;

/// <summary>
/// Writes the configured security headers when a response starts, keeping any value an endpoint already set.
/// HSTS is not written here; ASP.NET Core's <c>UseHsts()</c> does that, over HTTPS only.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly WebApiSecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<WebApiOptions> options)
    {
        _next = next;
        _options = options.Value.SecurityHeaders;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Registered before the rest of the pipeline runs, so error responses get the headers too. The endpoint is
        // known by the time the response starts, so its own Content-Security-Policy can be honored.
        context.Response.OnStarting(
            static state =>
            {
                var (httpContext, options) = ((HttpContext, WebApiSecurityHeadersOptions))state;
                Apply(httpContext, options);
                return Task.CompletedTask;
            },
            (context, _options));

        return _next(context);
    }

    private static void Apply(HttpContext context, WebApiSecurityHeadersOptions options)
    {
        var headers = context.Response.Headers;

        SetIfAbsent(headers, HeaderNames.XContentTypeOptions, options.ContentTypeOptions);
        SetIfAbsent(headers, HeaderNames.XFrameOptions, options.FrameOptions);
        SetIfAbsent(headers, PresentationHeaderNames.ReferrerPolicy, options.ReferrerPolicy);
        SetIfAbsent(headers, PresentationHeaderNames.PermissionsPolicy, options.PermissionsPolicy);

        var endpointPolicy = RequestFacts.GetEndpoint(context)?.Metadata.GetMetadata<ContentSecurityPolicyMetadata>();
        SetIfAbsent(headers, HeaderNames.ContentSecurityPolicy, endpointPolicy is null ? options.ContentSecurityPolicy : endpointPolicy.Policy);
    }

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}
