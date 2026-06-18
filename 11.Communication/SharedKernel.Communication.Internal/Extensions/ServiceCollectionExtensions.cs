using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using SharedKernel.Communication.Internal.Options;
using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Communication.Internal.Extensions;

/// <summary>
/// DI registration extensions for <c>SharedKernel.Communication.Internal</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="KubernetesServiceEndpointResolver"/> as the <see cref="IServiceEndpointResolver"/>
    /// singleton and wires <c>Microsoft.Extensions.ServiceDiscovery</c> DNS resolution.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to configure <see cref="K8sServiceDiscoveryOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddK8sServiceDiscovery(
        this IServiceCollection services,
        Action<K8sServiceDiscoveryOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<K8sServiceDiscoveryOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<K8sServiceDiscoveryOptions>, K8sServiceDiscoveryOptionsValidator>();

        // Wire Microsoft.Extensions.ServiceDiscovery core (registers ServiceEndpointResolver,
        // ServiceEndpointWatcherFactory, pass-through provider, etc.)
        services.AddServiceDiscoveryCore();

        services.TryAddSingleton<IServiceEndpointResolver, KubernetesServiceEndpointResolver>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="StaticServiceEndpointResolver"/> as the <see cref="IServiceEndpointResolver"/>
    /// singleton using a fixed endpoint map. <b>Dev/test environments only.</b>
    /// Logs <see cref="LogLevel.Warning"/> at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoints">
    /// Map of logical service name to base <see cref="Uri"/>
    /// (e.g. <c>"order-service" → new Uri("http://localhost:5001")</c>).
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="IServiceEndpointResolver"/> is already registered in the container.
    /// </exception>
    public static IServiceCollection AddStaticServiceDiscovery(
        this IServiceCollection services,
        Dictionary<string, Uri> endpoints)
    {
        // Guard: throw if IServiceEndpointResolver already registered.
        if (services.Any(d => d.ServiceType == typeof(IServiceEndpointResolver)))
            throw new InvalidOperationException(
                $"Cannot register StaticServiceEndpointResolver: {nameof(IServiceEndpointResolver)} is already registered. " +
                "Ensure AddStaticServiceDiscovery is called before any other service discovery extension " +
                "and that it is not used in a production environment alongside AddK8sServiceDiscovery.");

        IReadOnlyDictionary<string, Uri> readOnly = endpoints;
        services.AddSingleton<IServiceEndpointResolver>(new StaticServiceEndpointResolver(readOnly));

        // Emit a LogLevel.Warning at startup via a lightweight IHostedService.
        services.AddHostedService(sp => new StaticServiceDiscoveryStartupWarning(
            sp.GetRequiredService<ILogger<StaticServiceDiscoveryStartupWarning>>(),
            endpoints));

        return services;
    }
}
