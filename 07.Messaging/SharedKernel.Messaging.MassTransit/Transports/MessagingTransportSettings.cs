using MassTransit;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Options;

namespace SharedKernel.Messaging.MassTransit.Transports;

/// <summary>
/// What <see cref="MessagingBusBuilder"/> hands a <see cref="MessagingTransport"/>: the settings a transport
/// implements natively, and the platform pipeline every transport applies.
/// </summary>
public sealed class MessagingTransportSettings
{
    private readonly MessagingBusBuilder _builder;

    internal MessagingTransportSettings(bool delayedDelivery, DeadLetterOptions? deadLetterPolicy, MessagingBusBuilder builder)
    {
        DelayedDelivery = delayedDelivery;
        DeadLetterPolicy = deadLetterPolicy;
        _builder = builder;
    }

    /// <summary>
    /// Gets whether <see cref="MessagingBusBuilder.WithDelayedDelivery"/> was called. The transport registers its
    /// own broker-side scheduler; <c>IMessageScheduler</c> is registered by the builder.
    /// </summary>
    public bool DelayedDelivery { get; }

    /// <summary>
    /// Gets the dead-letter policy from <see cref="MessagingBusBuilder.WithDeadLetterPolicy"/>, or
    /// <see langword="null"/> when it was not called. A transport that cannot apply it says so at startup.
    /// </summary>
    public DeadLetterOptions? DeadLetterPolicy { get; }

    /// <summary>
    /// Applies the platform's bus pipeline — the inbound request-context filter, consumer idempotency, the
    /// payload transform, retry and the circuit breaker — and configures the receive endpoints. A transport calls
    /// this last, from inside its bus callback, after its own host settings.
    /// </summary>
    /// <typeparam name="TEndpointConfigurator">The transport's receive-endpoint configurator type.</typeparam>
    /// <param name="context">The bus registration context.</param>
    /// <param name="bus">The transport's bus factory configurator.</param>
    public void ConfigureBus<TEndpointConfigurator>(
        IBusRegistrationContext context,
        IBusFactoryConfigurator<TEndpointConfigurator> bus)
        where TEndpointConfigurator : IReceiveEndpointConfigurator
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bus);
        _builder.ConfigureBusPipeline(context, bus);
    }
}
