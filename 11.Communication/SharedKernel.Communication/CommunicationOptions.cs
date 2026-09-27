using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Communication;

/// <summary>
/// Settings shared by every outbound client of the service. Bound from <c>SharedKernel:Communication</c>; each
/// client's own settings are under <c>SharedKernel:Communication:Clients:{name}</c>.
/// </summary>
/// <example>
/// <code>
/// "SharedKernel": {
///   "Communication": {
///     "ServiceDiscovery": { "Mode": "Dns" },
///     "Clients": {
///       "inventory": { "BaseAddress": "http://inventory" }
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class CommunicationOptions : ISectionBoundOptions, IValidatableObject
{
    /// <summary>Gets the configuration section: <c>SharedKernel:Communication</c>.</summary>
    public static string SectionName => "SharedKernel:Communication";

    /// <summary>Gets the section under which each client's settings live: <c>SharedKernel:Communication:Clients</c>.</summary>
    public static string ClientsSectionName => SectionName + ":Clients";

    /// <summary>Gets or sets how a client's host name is turned into the endpoints it calls.</summary>
    public CommunicationDiscoveryOptions ServiceDiscovery { get; set; } = new();

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(ServiceDiscovery.Mode))
        {
            yield return new ValidationResult(
                $"ServiceDiscovery:Mode '{ServiceDiscovery.Mode}' is not one of {string.Join(", ", Enum.GetNames<ServiceDiscoveryMode>())}.",
                [nameof(ServiceDiscovery)]);
        }

        if (ServiceDiscovery.RefreshPeriod < TimeSpan.FromSeconds(1) || ServiceDiscovery.RefreshPeriod > TimeSpan.FromHours(1))
        {
            yield return new ValidationResult(
                "ServiceDiscovery:RefreshPeriod must be between 1 second and 1 hour.",
                [nameof(ServiceDiscovery)]);
        }
    }
}

/// <summary>
/// Service discovery: how the host of a client's address (<c>http://inventory</c>) becomes the endpoints the
/// requests go to. Built on <c>Microsoft.Extensions.ServiceDiscovery</c>.
/// </summary>
/// <remarks>
/// <para>
/// Whatever the mode, endpoints configured under the <c>Services</c> section win
/// (<c>"Services": { "inventory": { "http": [ "http://localhost:5080" ] } }</c>, the shape .NET Aspire and
/// <c>services__inventory__http__0</c> environment variables produce), and a host no provider knows is called as
/// written, resolved by the operating system: <c>http://inventory.shop.svc.cluster.local</c> works in every mode.
/// </para>
/// <para>
/// An address may name one of a service's endpoints (<c>http://_grpc.inventory</c> reads <c>Services:inventory:grpc</c>)
/// and may prefer a scheme (<c>https+http://inventory</c> uses HTTPS when an HTTPS endpoint exists).
/// </para>
/// </remarks>
public sealed class CommunicationDiscoveryOptions
{
    /// <summary>
    /// Gets or sets where endpoints come from beyond configuration. Defaults to
    /// <see cref="ServiceDiscoveryMode.Configuration"/>.
    /// </summary>
    public ServiceDiscoveryMode Mode { get; set; } = ServiceDiscoveryMode.Configuration;

    /// <summary>
    /// Gets or sets how often a service's endpoints are resolved again, so pods that come and go are picked up.
    /// Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the domain appended to a service name for a DNS SRV query in
    /// <see cref="ServiceDiscoveryMode.DnsSrv"/> mode (<c>shop.svc.cluster.local</c>). Leave empty inside Kubernetes:
    /// the pod's own namespace is read from the service account.
    /// </summary>
    public string? DnsSrvQuerySuffix { get; set; }
}

/// <summary>Where a client's endpoints come from, beyond the <c>Services</c> configuration section.</summary>
public enum ServiceDiscoveryMode
{
    /// <summary>
    /// Configuration only; any other host is called as written. Kubernetes' ClusterIP services balance each
    /// connection across pods, which suits HTTP/1.1. The default.
    /// </summary>
    Configuration = 0,

    /// <summary>
    /// DNS A/AAAA records: every address of a headless service's name is an endpoint, and requests are spread across
    /// them round-robin, per request. Use it for gRPC, whose long-lived HTTP/2 connections a ClusterIP service would pin
    /// to one pod. The original host name is kept for the <c>Host</c> header and TLS.
    /// </summary>
    Dns = 1,

    /// <summary>
    /// DNS SRV records (<c>_{endpoint}._tcp.{service}.{namespace}.svc.cluster.local</c>): the endpoints and their ports
    /// come from the records Kubernetes publishes for a service's named ports.
    /// </summary>
    DnsSrv = 2,
}
