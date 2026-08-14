namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// Configures <see cref="MtlsForwardedHeaderMiddleware"/> for hosts where TLS terminates at an
/// ingress/gateway ahead of Kestrel, which forwards the client certificate as a request header
/// instead of negotiating it directly.
/// </summary>
/// <remarks>
/// Bound from the configuration section <c>"SharedKernel:ServiceDefaults:MtlsForwardedHeader"</c>
/// via <see cref="MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/>.
/// </remarks>
public sealed class MtlsForwardedHeaderOptions
{
    /// <summary>The configuration section key for <see cref="MtlsForwardedHeaderOptions"/>.</summary>
    public const string SectionName = "SharedKernel:ServiceDefaults:MtlsForwardedHeader";

    /// <summary>
    /// Gets or sets the request header name the ingress/gateway forwards the client certificate
    /// under.
    /// </summary>
    /// <remarks>
    /// <b>Carries NO default value tied to any one ingress/gateway vendor's convention</b> —
    /// nginx-ingress commonly forwards a header named <c>ssl-client-cert</c>, Envoy/Istio commonly
    /// forward a structured <c>x-forwarded-client-cert</c>, and HAProxy deployments vary further. A
    /// host adopting this middleware MUST configure this value explicitly via
    /// <see cref="MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/>; an
    /// unconfigured (null/empty/whitespace) value fails fast at startup via options validation —
    /// it never silently falls back to a guessed literal.
    /// </remarks>
    public string HeaderName { get; set; } = string.Empty;
}
