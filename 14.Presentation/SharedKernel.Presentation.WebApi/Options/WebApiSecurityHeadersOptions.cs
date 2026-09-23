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
/// Development environment.
/// </para>
/// </remarks>
public sealed class WebApiSecurityHeadersOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether security headers, HSTS included, are written. Defaults to
    /// <see langword="true"/>.
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
}
