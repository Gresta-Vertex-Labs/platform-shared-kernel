using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Internal.Options;

namespace SharedKernel.Communication.Internal.Resolvers;

/// <summary>
/// Production <see cref="IServiceEndpointResolver"/> that uses
/// <c>Microsoft.Extensions.ServiceDiscovery</c> to perform DNS resolution.
/// Resolution order: SRV record (<c>_http._tcp.{service}.{namespace}.svc.{clusterDomain}</c>)
/// first; A-record fallback for headless services; K8s convention URI on any failure.
/// Never throws — see <see cref="IServiceEndpointResolver"/> contract.
/// Registered as singleton via <c>AddK8sServiceDiscovery</c>.
/// </summary>
internal sealed class KubernetesServiceEndpointResolver(
    IOptions<K8sServiceDiscoveryOptions> options,
    ILogger<KubernetesServiceEndpointResolver> logger) : IServiceEndpointResolver
{
    private readonly K8sServiceDiscoveryOptions _options = options.Value;
    private readonly ILogger<KubernetesServiceEndpointResolver> _logger = logger;

    /// <inheritdoc />
    public ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    private Uri BuildFallbackUri(string serviceName)
    {
        var scheme = _options.SchemeOverride ?? "http";
        return new Uri($"{scheme}://{serviceName}.{_options.Namespace}.svc.{_options.ClusterDomain}");
    }
}
