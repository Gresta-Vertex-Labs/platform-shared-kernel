using SharedKernel.Configuration;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Settings for the HTTP API boundary set up by
/// <see cref="WebApiHostBuilderExtensions.AddSharedKernelWebApi"/>, bound from
/// <c>SharedKernel:Presentation:WebApi</c> and validated before the service handles a request.
/// </summary>
/// <remarks>
/// <para>
/// Every default is the secure, production-ready choice, so a service that configures nothing gets correlation
/// ids, security headers, a 4 MiB request body limit, no cross-origin access, uncached responses and redacted server
/// errors.
/// </para>
/// <para>
/// The <c>configure</c> callback of <see cref="WebApiHostBuilderExtensions.AddSharedKernelWebApi"/> runs after
/// binding, so code can override configuration. Configuration values for a list that has defaults (such as
/// <see cref="WebApiCorsOptions.ExposedHeaders"/>) are added to the defaults; clear the list in the callback to
/// replace them.
/// </para>
/// <para>
/// Invalid settings throw <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> the first time they
/// are read: with Kestrel when the host is built (<c>builder.Build()</c>, which configures the server), otherwise —
/// for example with <c>TestServer</c> — when <c>UseSharedKernelWebApi()</c> builds the pipeline, and at the latest
/// when the host starts.
/// </para>
/// </remarks>
public sealed class SharedKernelWebApiOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section these settings bind from: <c>SharedKernel:Presentation:WebApi</c>.</summary>
    public static string SectionName => "SharedKernel:Presentation:WebApi";

    /// <summary>Gets the correlation id settings.</summary>
    public WebApiCorrelationIdOptions CorrelationId { get; } = new();

    /// <summary>Gets the cross-origin resource sharing settings. No origin is allowed by default.</summary>
    public WebApiCorsOptions Cors { get; } = new();

    /// <summary>Gets the security response header settings, HSTS and the default <c>Cache-Control</c> included.</summary>
    public WebApiSecurityHeadersOptions SecurityHeaders { get; } = new();

    /// <summary>Gets the request limits.</summary>
    public WebApiLimitsOptions Limits { get; } = new();

    /// <summary>Gets the error response settings.</summary>
    public WebApiProblemsOptions Problems { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether Kestrel's <c>Server</c> response header is removed. Defaults to
    /// <see langword="true"/>: naming the server software helps nobody but an attacker.
    /// </summary>
    public bool RemoveServerHeader { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether W3C <c>baggage</c> sent by the caller is kept. Defaults to
    /// <see langword="false"/>: hosting takes no baggage from the request, and the first thing the pipeline does is
    /// remove any inbound item still on the request's <see cref="System.Diagnostics.Activity"/>, before the correlation
    /// id is added to it.
    /// </summary>
    /// <remarks>
    /// Baggage travels onward with every outgoing call and is copied onto log records, so a caller who can set it
    /// can plant values — a tenant id, a user id — that downstream services and log queries trust. Set this to
    /// <see langword="true"/> only behind a gateway that removes or rewrites caller-supplied baggage. Baggage this
    /// service adds itself, the correlation id included, is never affected.
    /// </remarks>
    public bool TrustInboundBaggage { get; set; }
}
