using MassTransit;
using SharedKernel.Messaging.MassTransit.Options;
using SharedKernel.Messaging.MassTransit.Transports;

namespace SharedKernel.Messaging.MassTransit.RabbitMq.Transport;

/// <summary>
/// The RabbitMQ <see cref="MessagingTransport"/>. A partition key maps to the routing key, the base default.
/// </summary>
internal sealed class RabbitMqMessagingTransport : MessagingTransport
{
    private readonly Action<IRabbitMqBusFactoryConfigurator> _configureHost;

    /// <summary>Initializes the transport.</summary>
    /// <param name="configureHost">Applies the host connection settings.</param>
    public RabbitMqMessagingTransport(Action<IRabbitMqBusFactoryConfigurator> configureHost)
        : base("rabbitmq")
    {
        _configureHost = configureHost;
    }

    /// <inheritdoc />
    public override void Configure(IBusRegistrationConfigurator configurator, MessagingTransportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(settings);

        // SC-06: the transport-native scheduler (the delayed-message exchange). Registers
        // MassTransit.IMessageScheduler, which MassTransitMessageScheduler wraps.
        if (settings.DelayedDelivery)
            configurator.AddDelayedMessageScheduler();

        configurator.UsingRabbitMq((ctx, busCfg) =>
        {
            _configureHost(busCfg);

            // SC-06: Wire the RabbitMQ delayed-message exchange for deferred delivery.
            if (settings.DelayedDelivery)
                busCfg.UseDelayedMessageScheduler();

            // P-343: Apply the dead-letter policy's message TTL to the automatically-derived
            // fault/dead-letter queues.
            if (settings.DeadLetterPolicy is { } deadLetter)
                ConfigureDeadLetterPolicy(busCfg, deadLetter);

            settings.ConfigureBus(ctx, busCfg);
        });
    }

    // Internal (not private) so ConcurrencyLimitConfigurationTests can exercise this helper
    // in isolation via a substituted IRabbitMqBusFactoryConfigurator (P-342/WO-054).
    internal static void ConfigureRabbitMq(IRabbitMqBusFactoryConfigurator cfg, RabbitMqBusOptions opts)
    {
        cfg.Host(opts.Host, opts.VirtualHost, h =>
        {
            h.Username(opts.Username);
            h.Password(opts.Password);
            h.Heartbeat(opts.RequestedHeartbeat);
        });

        cfg.PrefetchCount = opts.Prefetch;

        // P-342/WO-054: Optional bus-level default concurrency ceiling, distinct from PrefetchCount.
        // A per-consumer override on ConsumerDefinitionBase<TConsumer>.ConcurrentMessageLimit takes
        // precedence over this default on that consumer's own endpoint.
        if (opts.ConcurrentMessageLimit.HasValue)
            cfg.ConcurrentMessageLimit = opts.ConcurrentMessageLimit.Value;
    }

    // Internal (not private) so DeadLetterPolicyConfigurationTests can exercise this helper
    // in isolation via a substituted IRabbitMqBusFactoryConfigurator (P-343/WO-054).
    internal static void ConfigureDeadLetterPolicy(IRabbitMqBusFactoryConfigurator cfg, DeadLetterOptions opts)
    {
        // Capability note: IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings/
        // .ConfigureDeadLetterSettings configure the ARGUMENTS of the automatically-derived fault
        // ("_error") and dead-letter ("_skipped") queues, the same settings RabbitMqReceiveEndpointBuilder
        // uses to build the real fault transport a faulted/retry-exhausted message is routed to.
        // There is no public hook to rename those queues, so DeadLetterOptions.QueueNameSuffix is
        // accepted but has no observable effect; see its own XML doc. Only MessageTimeToLive is wired.
        if (!opts.MessageTimeToLive.HasValue)
            return;

        var timeToLive = opts.MessageTimeToLive.Value;

        cfg.SendTopology.ConfigureErrorSettings = queue => queue.SetQueueArgument("x-message-ttl", timeToLive);
        cfg.SendTopology.ConfigureDeadLetterSettings = queue => queue.SetQueueArgument("x-message-ttl", timeToLive);
    }
}
