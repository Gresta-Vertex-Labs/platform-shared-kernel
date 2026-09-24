namespace SharedKernel.Presentation.WebApi.Options;

/// <summary>Security response headers written on every response.</summary>
/// <remarks>
/// <para>
/// A header whose value is <see langword="null"/> or empty is not written, and a header an endpoint already set is
/// never overwritten. The default <see cref="ContentSecurityPolicy"/> suits a JSON API; an endpoint that serves a
/// page (an API reference UI, for example) sets its own with <c>WithContentSecurityPolicy</c>.
/// </para>
/// <para>
/// HSTS uses ASP.NET Core's own middleware: it is sent over HTTPS only, never to <c>localhost</c>, and never in the
/// Development environment. It runs before the exception handler and survives the handler clearing the response, so
/// error responses carry it too. Behind a proxy that terminates TLS, add forwarded-headers handling with the
/// <c>AtStart</c> hook of <c>UseSharedKernelWebApi</c>, or the request never looks like HTTPS.
/// </para>
/// </remarks>
public sealed class WebApiSecurityHeadersOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether security headers, HSTS and the default <see cref="CacheControl"/>
    /// included, are written. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether <c>Strict-Transport-Security</c> is sent. Defaults to <see langword="true"/>.</summary>
    public bool Hsts { get; set; } = true;

    /// <summary>Gets or sets the HSTS <c>max-age</c>. Defaults to 365 days.</summary>
    public TimeSpan HstsMaxAge { get; set; } = TimeSpan.FromDays(365);

    /// <summary>Gets or sets a value indicating whether HSTS covers subdomains. Defaults to <see langword="true"/>.</summary>
    public bool HstsIncludeSubDomains { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the HSTS <c>preload</c> directive is sent. Defaults to
    /// <see langword="false"/>; preloading is hard to undo, so opt in deliberately.
    /// </summary>
    public bool HstsPreload { get; set; }

    /// <summary>Gets or sets the <c>X-Content-Type-Options</c> value. Defaults to <c>nosniff</c>.</summary>
    public string? ContentTypeOptions { get; set; } = "nosniff";

    /// <summary>Gets or sets the <c>X-Frame-Options</c> value. Defaults to <c>DENY</c>.</summary>
    public string? FrameOptions { get; set; } = "DENY";

    /// <summary>Gets or sets the <c>Referrer-Policy</c> value. Defaults to <c>no-referrer</c>.</summary>
    public string? ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary>
    /// Gets or sets the <c>Permissions-Policy</c> value. Defaults to <c>geolocation=(), microphone=(), camera=()</c>.
    /// </summary>
    public string? PermissionsPolicy { get; set; } = "geolocation=(), microphone=(), camera=()";

    /// <summary>
    /// Gets or sets the <c>Content-Security-Policy</c> value. Defaults to <c>default-src 'none'; frame-ancestors 'none'</c>,
    /// which lets a browser load nothing from an API response and forbids framing it.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Gets or sets the <c>Cache-Control</c> value written on a response that sets neither <c>Cache-Control</c> nor
    /// <c>ETag</c>. Defaults to <c>no-store</c>, so browsers and shared proxies keep no copy of API data;
    /// <see langword="null"/> or empty writes nothing.
    /// </summary>
    /// <remarks>
    /// A response that sets its own <c>Cache-Control</c> keeps it, and one with an <c>ETag</c> (such as
    /// <c>ToOkWithETag(…)</c>) is left alone so clients can revalidate it. Error responses
    /// (<c>application/problem+json</c>) are always <c>no-store</c>, whatever this value is.
    /// </remarks>
    public string? CacheControl { get; set; } = "no-store";
}
