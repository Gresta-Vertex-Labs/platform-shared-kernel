using System.Net;
using System.Net.Sockets;

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
    private readonly List<IPNetwork> _trustedNetworks = [];

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

    /// <summary>
    /// Gets the set of IP networks trusted to set the forwarded certificate header. Defaults to
    /// empty (unrestricted — preserves the pre-P-394 behavior).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opt-in trust-boundary allowlist (WO-061/P-394), mirroring ASP.NET Core's own
    /// <c>ForwardedHeadersOptions.KnownProxies</c>/<c>KnownNetworks</c> shape but unified into one
    /// collection type, since <see cref="IPNetwork"/> (BCL since .NET 8) already expresses a single
    /// trusted proxy IP as a <c>/32</c> or <c>/128</c> network.
    /// </para>
    /// <para>
    /// When non-empty, <see cref="MtlsForwardedHeaderMiddleware"/> ignores — never decodes,
    /// validates, or sets <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.ClientCertificate"/>
    /// for — a forwarded certificate header from a remote IP outside this allowlist, regardless of
    /// whether the certificate itself would otherwise validate. This closes the trust-boundary gap
    /// where any network path reaching this host directly (a misconfigured <c>NetworkPolicy</c>, a
    /// multi-hop mesh topology, a debug port, a compromised sidecar) could forge the header
    /// identically to the real ingress.
    /// </para>
    /// <para>
    /// When left unconfigured (the default), existing unrestricted-header behavior is preserved,
    /// but a one-time startup <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> states
    /// that any network path reaching this host directly can forge the header. Additive/opt-in
    /// only — a host that does not configure <see cref="TrustedNetworks"/> is functionally
    /// unchanged aside from the new warning log.
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<IPNetwork> TrustedNetworks => _trustedNetworks;

    /// <summary>
    /// Adds a single trusted proxy address (expressed internally as a <c>/32</c> or <c>/128</c>
    /// network, matching only that exact address) to <see cref="TrustedNetworks"/>.
    /// </summary>
    /// <param name="address">The single IP address to trust.</param>
    /// <returns>The same <see cref="MtlsForwardedHeaderOptions"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// The single-address counterpart to <see cref="AddTrustedNetwork"/> — mirrors the role ASP.NET
    /// Core's own <c>ForwardedHeadersOptions.KnownProxies</c> plays alongside <c>KnownNetworks</c>
    /// (a single trusted proxy vs. a trusted CIDR range), unified here into one <see cref="IPNetwork"/>
    /// collection since a single address is just a <c>/32</c>/<c>/128</c> network.
    /// </remarks>
    public MtlsForwardedHeaderOptions AddTrustedProxy(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var prefixLength = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        _trustedNetworks.Add(new IPNetwork(address, prefixLength));
        return this;
    }

    /// <summary>
    /// Adds a trusted IP network (CIDR range) to <see cref="TrustedNetworks"/>.
    /// </summary>
    /// <param name="network">The network to trust.</param>
    /// <returns>The same <see cref="MtlsForwardedHeaderOptions"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// The CIDR-range counterpart to <see cref="AddTrustedProxy"/> — mirrors the role ASP.NET Core's
    /// own <c>ForwardedHeadersOptions.KnownNetworks</c> plays alongside <c>KnownProxies</c> (a trusted
    /// subnet, e.g. an ingress controller's pod CIDR, vs. a single trusted proxy address).
    /// </remarks>
    public MtlsForwardedHeaderOptions AddTrustedNetwork(IPNetwork network)
    {
        _trustedNetworks.Add(network);
        return this;
    }
}
