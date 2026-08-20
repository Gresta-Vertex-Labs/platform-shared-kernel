namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// Configures the platform-default HTTP response security headers written by
/// <see cref="SecurityHeadersMiddleware"/>.
/// </summary>
/// <remarks>
/// Every header is individually toggle-able and value-configurable via its own nested options
/// object. <see cref="Hsts"/> is opt-<b>out</b> — it ships enabled by default, unlike every other
/// header on this type.
/// <para>
/// <b>HSTS WARNING:</b> HSTS IS ENABLED BY DEFAULT. IT MUST BE DISABLED (SET
/// <c>options.Hsts.Enabled = false</c>) OR GIVEN A SHORT <see cref="HstsHeaderOptions.MaxAge"/> FOR
/// LOCAL HTTP-ONLY DEVELOPMENT — A BROWSER THAT CACHES A LONG-LIVED HSTS POLICY FOR A HOST WILL
/// REFUSE PLAIN HTTP CONNECTIONS TO THAT HOST UNTIL THE POLICY EXPIRES, MIRRORING ASP.NET CORE'S
/// OWN <c>UseHsts()</c> GUIDANCE.
/// </para>
/// </remarks>
public sealed class SecurityHeadersOptions
{
    /// <summary>Gets the <c>Strict-Transport-Security</c> header configuration. Enabled by default.</summary>
    public HstsHeaderOptions Hsts { get; } = new();

    /// <summary>Gets the <c>X-Content-Type-Options</c> header configuration. Enabled by default.</summary>
    public ContentTypeOptionsHeaderOptions ContentTypeOptions { get; } = new();

    /// <summary>Gets the <c>X-Frame-Options</c> header configuration. Enabled by default.</summary>
    public FrameOptionsHeaderOptions FrameOptions { get; } = new();

    /// <summary>Gets the <c>Referrer-Policy</c> header configuration. Enabled by default.</summary>
    public ReferrerPolicyHeaderOptions ReferrerPolicy { get; } = new();

    /// <summary>Gets the <c>Permissions-Policy</c> header configuration. Enabled by default.</summary>
    public PermissionsPolicyHeaderOptions PermissionsPolicy { get; } = new();

    /// <summary>
    /// Gets the configured <c>Content-Security-Policy</c> header value, or <see langword="null"/>
    /// when no policy has been configured.
    /// </summary>
    /// <remarks>
    /// No default CSP value exists — CSP is response-shape-specific (a pure JSON API vs. one also
    /// serving Scalar's interactive UI) and a wrong default could break this package's own
    /// <c>MapSharedKernelOpenApi</c>/Scalar UI. The only way this is ever non-<see langword="null"/>
    /// is a call to <see cref="WithContentSecurityPolicy(string)"/> or
    /// <see cref="WithContentSecurityPolicy(Action{CspBuilder})"/>.
    /// </remarks>
    public string? ContentSecurityPolicy { get; private set; }

    /// <summary>
    /// Sets the <c>Content-Security-Policy</c> header value to the specified raw
    /// <paramref name="policy"/> string.
    /// </summary>
    /// <param name="policy">The complete, already-formatted CSP directive string.</param>
    /// <returns>This <see cref="SecurityHeadersOptions"/> instance, for chaining.</returns>
    public SecurityHeadersOptions WithContentSecurityPolicy(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ContentSecurityPolicy = policy;
        return this;
    }

    /// <summary>
    /// Builds the <c>Content-Security-Policy</c> header value from a fluent
    /// <see cref="CspBuilder"/> configuration.
    /// </summary>
    /// <param name="configure">A callback that adds one or more CSP directives.</param>
    /// <returns>This <see cref="SecurityHeadersOptions"/> instance, for chaining.</returns>
    public SecurityHeadersOptions WithContentSecurityPolicy(Action<CspBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new CspBuilder();
        configure(builder);
        ContentSecurityPolicy = builder.Build();
        return this;
    }
}

/// <summary>Configures the <c>Strict-Transport-Security</c> response header.</summary>
public sealed class HstsHeaderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the header is written. Defaults to
    /// <see langword="true"/> — HSTS is opt-<b>out</b>, unlike every other header on
    /// <see cref="SecurityHeadersOptions"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the <c>max-age</c> directive value. Defaults to 365 days.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(365);

    /// <summary>Gets or sets whether the <c>includeSubDomains</c> directive is appended. Defaults to <see langword="true"/>.</summary>
    public bool IncludeSubDomains { get; set; } = true;

    /// <summary>Gets or sets whether the <c>preload</c> directive is appended. Defaults to <see langword="false"/>.</summary>
    public bool Preload { get; set; }
}

/// <summary>Configures the <c>X-Content-Type-Options</c> response header.</summary>
public sealed class ContentTypeOptionsHeaderOptions
{
    /// <summary>Gets or sets a value indicating whether the header is written. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Configures the <c>X-Frame-Options</c> response header.</summary>
public sealed class FrameOptionsHeaderOptions
{
    /// <summary>Gets or sets a value indicating whether the header is written. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the header value. Defaults to <c>"DENY"</c>.</summary>
    public string Value { get; set; } = "DENY";
}

/// <summary>Configures the <c>Referrer-Policy</c> response header.</summary>
public sealed class ReferrerPolicyHeaderOptions
{
    /// <summary>Gets or sets a value indicating whether the header is written. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the header value. Defaults to <c>"strict-origin-when-cross-origin"</c>.</summary>
    public string Value { get; set; } = "strict-origin-when-cross-origin";
}

/// <summary>Configures the <c>Permissions-Policy</c> response header.</summary>
public sealed class PermissionsPolicyHeaderOptions
{
    /// <summary>Gets or sets a value indicating whether the header is written. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the header value. Defaults to a conservative policy that disables geolocation,
    /// microphone, and camera access.
    /// </summary>
    public string Value { get; set; } = "geolocation=(), microphone=(), camera=()";
}

/// <summary>
/// Fluent builder for a <c>Content-Security-Policy</c> header value, used by
/// <see cref="SecurityHeadersOptions.WithContentSecurityPolicy(Action{CspBuilder})"/>.
/// </summary>
public sealed class CspBuilder
{
    private readonly Dictionary<string, string> _directives = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds or replaces a CSP directive (e.g. <c>"default-src"</c>, <c>"script-src"</c>).
    /// </summary>
    /// <param name="directive">The directive name.</param>
    /// <param name="sources">The directive's space-separated source list value.</param>
    /// <returns>This <see cref="CspBuilder"/> instance, for chaining.</returns>
    public CspBuilder AddDirective(string directive, string sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directive);
        ArgumentException.ThrowIfNullOrWhiteSpace(sources);
        _directives[directive] = sources;
        return this;
    }

    /// <summary>Builds the semicolon-joined CSP header value from every added directive.</summary>
    /// <returns>The complete CSP header value.</returns>
    public string Build() => string.Join("; ", _directives.Select(directive => $"{directive.Key} {directive.Value}"));
}
