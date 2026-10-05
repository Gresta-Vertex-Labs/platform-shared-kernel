using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using SharedKernel.Communication.Internal;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Communication;

/// <summary>Registers SharedKernel's outbound communication.</summary>
public static class CommunicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers what every outbound client shares — service discovery, the request-context accessor, the
    /// client-credentials token cache — with <see cref="CommunicationOptions"/> bound from <c>SharedKernel:Communication</c>
    /// and validated at startup. Chain <c>AddRestClient</c> and <c>AddGrpcClient</c> on the result.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration; every client's settings are read from it.</param>
    /// <param name="configure">Changes <see cref="CommunicationOptions"/> after binding, when code must decide.</param>
    /// <returns>The builder to add clients to.</returns>
    /// <remarks>
    /// Calling it again returns a builder over the same registrations, so a module may call it on its own. The service
    /// discovery mode is read once, here.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSharedKernelCommunication(builder.Configuration)
    ///     .AddRestClient&lt;IInventoryClient, InventoryClient&gt;("inventory")
    ///     .AddGrpcClient&lt;Pricing.PricingClient&gt;("pricing");
    /// </code>
    /// </example>
    public static ICommunicationBuilder AddSharedKernelCommunication(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CommunicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = new CommunicationBuilder(services, configuration);
        if (CommunicationClientRegistry.Find(services) is not null)
        {
            return builder;
        }

        services.AddSingleton(new CommunicationClientRegistry());
        services.AddValidatedOptions<CommunicationOptions>(configuration);
        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        services.TryAddSingleton<IClock, SystemClock>();

        // The Services-section endpoint provider reads IConfiguration from the container; a host already registers it.
        services.TryAddSingleton(configuration);

        var options = configuration.GetSection(CommunicationOptions.SectionName).Get<CommunicationOptions>() ?? new CommunicationOptions();
        configure?.Invoke(options);
        AddServiceDiscovery(services, options.ServiceDiscovery);
        AddTokenClient(services);

        return builder;
    }

    private static void AddServiceDiscovery(IServiceCollection services, CommunicationDiscoveryOptions discovery)
    {
        // Order is precedence: configured endpoints, then DNS when enabled, then the host as written.
        services.AddServiceDiscoveryCore(o => o.RefreshPeriod = discovery.RefreshPeriod);
        services.AddConfigurationServiceEndpointProvider();

        switch (discovery.Mode)
        {
            case ServiceDiscoveryMode.Dns:
                services.AddDnsServiceEndpointProvider(o =>
                {
                    o.DefaultRefreshPeriod = discovery.RefreshPeriod;
                    // Requests go to a pod's address; the Host header and the TLS server name stay the service's name.
                    o.ShouldApplyHostNameMetadata = _ => true;
                });
                break;
            case ServiceDiscoveryMode.DnsSrv:
                services.AddDnsSrvServiceEndpointProvider(o =>
                {
                    o.DefaultRefreshPeriod = discovery.RefreshPeriod;
                    o.ShouldApplyHostNameMetadata = _ => true;
                    if (!string.IsNullOrWhiteSpace(discovery.DnsSrvQuerySuffix))
                    {
                        o.QuerySuffix = discovery.DnsSrvQuerySuffix;
                    }
                });
                break;
        }

        services.AddPassThroughServiceEndpointProvider();
    }

    private static void AddTokenClient(IServiceCollection services)
    {
        services
            .AddHttpClient(ClientCredentialsTokenClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler()
            .Configure(o =>
            {
                // A token request (a POST) is idempotent, so it is retried; the call waiting on it gets an answer within 30 s.
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                o.Retry.MaxRetryAttempts = 2;
            });

        services.TryAddSingleton<ClientCredentialsTokenClient>();
    }

    private sealed class CommunicationBuilder(IServiceCollection services, IConfiguration configuration) : ICommunicationBuilder
    {
        public IServiceCollection Services { get; } = services;

        public IConfiguration Configuration { get; } = configuration;
    }
}
