using Microsoft.Net.Http.Headers;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Cross-origin resource sharing settings, applied to every endpoint, hubs included.</summary>
/// <remarks>
/// <para>
/// Deny by default: no policy exists until <see cref="AllowedOrigins"/> names at least one origin. Origins are
/// configuration, one list per environment, never code.
/// </para>
/// <para>
/// Startup validation refuses settings a browser would reject or an attacker could use:
/// </para>
/// <list type="bullet">
///   <item><see cref="AllowCredentials"/> with no origin or with <c>*</c>: browsers refuse that combination, and
///   letting it reach production only moves the failure to the first credentialed request.</item>
///   <item>The origin <c>null</c>, which sandboxed frames and <c>file://</c> pages send: allowing it allows them all.</item>
///   <item>Outside the Development environment, <see cref="AllowCredentials"/> with an <c>http://</c> origin: anyone
///   on the network path can inject script into a page served over plain HTTP and call the API with the user's
///   cookies.</item>
/// </list>
/// <para>
/// Browsers do not apply CORS to WebSockets. When origins are configured, a WebSocket request (a SignalR hub's
/// WebSocket transport, for example) whose <c>Origin</c> the policy does not allow is therefore refused with 403
/// <c>forbidden.origin_not_allowed</c> before it reaches the endpoint; a request without <c>Origin</c> — not a
/// browser — is let through.
/// </para>
/// </remarks>
public sealed class WebApiCorsOptions
{
    /// <summary>
    /// Gets the origins allowed to call the API, such as <c>https://app.example.com</c>. <c>*</c> allows any
    /// origin. Empty (the default) allows none.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>Gets the allowed request methods. Empty (the default) allows any method.</summary>
    public IList<string> AllowedMethods { get; } = [];

    /// <summary>Gets the allowed request headers. Empty (the default) allows any header.</summary>
    public IList<string> AllowedHeaders { get; } = [];

    /// <summary>
    /// Gets the response headers a browser script may read. Defaults to the headers this platform sends:
    /// <c>X-Correlation-Id</c>, <c>ETag</c>, <c>Location</c>, <c>Retry-After</c>, <c>Sunset</c>, <c>Deprecation</c>,
    /// <c>Link</c>, <c>api-supported-versions</c> and <c>api-deprecated-versions</c>.
    /// </summary>
    /// <remarks>Configured values are added to the defaults; clear the list in code to replace them.</remarks>
    public IList<string> ExposedHeaders { get; } =
    [
        WellKnownHeaders.CorrelationId,
        HeaderNames.ETag,
        HeaderNames.Location,
        HeaderNames.RetryAfter,
        PresentationHeaderNames.Sunset,
        PresentationHeaderNames.Deprecation,
        HeaderNames.Link,
        PresentationHeaderNames.ApiSupportedVersions,
        PresentationHeaderNames.ApiDeprecatedVersions,
    ];

    /// <summary>
    /// Gets or sets a value indicating whether browsers may send cookies or HTTP authentication with cross-origin
    /// requests. Defaults to <see langword="false"/>. Requires explicit origins.
    /// </summary>
    public bool AllowCredentials { get; set; }

    /// <summary>Gets or sets how long a browser may cache a preflight response. Defaults to 10 minutes.</summary>
    public TimeSpan PreflightMaxAge { get; set; } = TimeSpan.FromMinutes(10);
}
