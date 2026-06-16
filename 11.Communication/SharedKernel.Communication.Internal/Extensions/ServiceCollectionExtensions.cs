using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Internal.Options;

namespace SharedKernel.Communication.Internal.Extensions;

/// <summary>
/// DI registration extensions for <c>SharedKernel.Communication.Internal</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>KubernetesServiceEndpointResolver</c> as the <c>IServiceEndpointResolver</c> singleton
    /// and wires <c>Microsoft.Extensions.ServiceDiscovery</c> DNS resolution.
    /// </summary>
    public static IServiceCollection AddK8sServiceDiscovery(
        this IServiceCollection services,
        Action<K8sServiceDiscoveryOptions>? configure = null)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    /// <summary>
    /// Registers <c>StaticServiceEndpointResolver</c> as the <c>IServiceEndpointResolver</c> singleton.
    /// Dev/test environments only — logs <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> at startup.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <c>IServiceEndpointResolver</c> is already registered in the container.
    /// </exception>
    public static IServiceCollection AddStaticServiceDiscovery(
        this IServiceCollection services,
        Dictionary<string, Uri> endpoints)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
