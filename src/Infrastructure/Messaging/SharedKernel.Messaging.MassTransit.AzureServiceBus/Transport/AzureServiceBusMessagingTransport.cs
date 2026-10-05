using Azure.Identity;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Messaging.MassTransit.AzureServiceBus.DeadLetter;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Transports;

namespace SharedKernel.Messaging.MassTransit.AzureServiceBus.Transport;

/// <summary>
/// The Azure Service Bus <see cref="MessagingTransport"/>.
/// </summary>
internal sealed class AzureServiceBusMessagingTransport : MessagingTransport
{
    private readonly AzureServiceBusOptions _options;

    /// <summary>Initializes the transport.</summary>
    /// <param name="options">The connection settings.</param>
    public AzureServiceBusMessagingTransport(AzureServiceBusOptions options)
        : base("azure-service-bus")
    {
        _options = options;
    }

    /// <inheritdoc />
    public override void Configure(IBusRegistrationConfigurator configurator, MessagingTransportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(settings);

        // SC-06: the ASB-native scheduler (ScheduledEnqueueTimeUtc). Registers MassTransit.IMessageScheduler,
        // which MassTransitMessageScheduler wraps.
        if (settings.DelayedDelivery)
            configurator.AddServiceBusMessageScheduler();

        configurator.UsingAzureServiceBus((ctx, busCfg) =>
        {
            ConfigureAzureServiceBus(busCfg, _options);

            // SC-06: Wire ASB native scheduled delivery (ScheduledEnqueueTimeUtc).
            if (settings.DelayedDelivery)
                busCfg.UseServiceBusMessageScheduler();

            settings.ConfigureBus(ctx, busCfg);
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// P-343: <c>DeadLetterOptions</c> is RabbitMQ-only; Azure Service Bus dead-lettering is transport-native.
    /// A dead-letter policy is therefore a no-op here, reported by an advisory warning at startup.
    /// </remarks>
    public override void ConfigureServices(IServiceCollection services, MessagingTransportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.DeadLetterPolicy is not null)
            services.AddSingleton<IHostedService, DeadLetterPolicyAdvisoryHostedService>();
    }

    /// <summary>
    /// Sets both the routing key and the Azure Service Bus session identifier. The receiving endpoint must have
    /// sessions enabled for the ordering guarantee to hold — enabling sessions on an endpoint is a
    /// consuming-service/infrastructure responsibility this package does not silently apply retroactively.
    /// </summary>
    /// <param name="context">The outgoing send or publish context.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <remarks>
    /// Neither call throws when the transport-specific payload is absent:
    /// <see cref="ServiceBusSendContextExtensions.SetSessionId"/> is a silent no-op without a
    /// <see cref="ServiceBusSendContext"/> payload (P-344/WO-054).
    /// </remarks>
    public override void ApplyPartitionKey(SendContext context, string partitionKey)
    {
        base.ApplyPartitionKey(context, partitionKey);
        ServiceBusSendContextExtensions.SetSessionId(context, partitionKey);
    }

    // Internal (not private) so ConcurrencyLimitConfigurationTests can exercise this helper
    // in isolation via a substituted IServiceBusBusFactoryConfigurator (P-342/WO-054).
    internal static void ConfigureAzureServiceBus(
        IServiceBusBusFactoryConfigurator cfg,
        AzureServiceBusOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace))
        {
            // Managed identity path — construct service URI from the fully qualified namespace.
            var serviceUri = new Uri($"sb://{opts.FullyQualifiedNamespace}");
            cfg.Host(serviceUri, h =>
            {
                h.TokenCredential = new DefaultAzureCredential();
                h.TransportType = opts.TransportType;
            });
        }
        else if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            cfg.Host(opts.ConnectionString, h =>
            {
                h.TransportType = opts.TransportType;
            });
        }
        else
        {
            throw new InvalidOperationException(
                "AzureServiceBusOptions requires exactly one of ConnectionString or FullyQualifiedNamespace to be set.");
        }

        // P-342/WO-054: Apply the bus-level receive endpoint concurrency default.
        // IServiceBusEndpointConfigurator.MaxConcurrentCalls is obsolete ("Set ConcurrentMessageLimit instead");
        // ConcurrentMessageLimit (from the core IBusFactoryConfigurator) is the current equivalent.
        cfg.ConcurrentMessageLimit = opts.MaxConcurrentCalls;
    }

    internal static void Validate(AzureServiceBusOptions opts)
    {
        var hasConnectionString = !string.IsNullOrWhiteSpace(opts.ConnectionString);
        var hasNamespace = !string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace);

        if (hasConnectionString && hasNamespace)
            throw new InvalidOperationException(
                "AzureServiceBusOptions: ConnectionString and FullyQualifiedNamespace are mutually exclusive. Set exactly one.");

        if (!hasConnectionString && !hasNamespace)
            throw new InvalidOperationException(
                "AzureServiceBusOptions: Either ConnectionString or FullyQualifiedNamespace must be set.");
    }
}
