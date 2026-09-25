using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Messaging.MassTransit.Transports;

/// <summary>
/// A broker transport that <see cref="Extensions.MessagingBusBuilder"/> can run on.
/// </summary>
/// <remarks>
/// <para>
/// The core package ships no transport, so it references no broker client. Each transport is its own
/// package and plugs in through <see cref="Extensions.MessagingBusBuilder.UseTransport"/>:
/// <c>SharedKernel.Messaging.MassTransit.RabbitMq</c> (<c>UseRabbitMq</c>) and
/// <c>SharedKernel.Messaging.MassTransit.AzureServiceBus</c> (<c>UseAzureServiceBus</c>). A service references
/// only the transport it runs on (P-570).
/// </para>
/// <para>
/// A transport owns what differs between brokers: the host connection, the transport-native delayed-delivery
/// scheduler, broker-specific policies and how a partition key becomes ordered delivery. Everything else —
/// the inbound request-context filter, consumer idempotency, the payload transform, retry, the circuit
/// breaker and endpoint configuration — is applied by <see cref="MessagingTransportSettings.ConfigureBus"/>,
/// so every transport gets the same pipeline.
/// </para>
/// </remarks>
public abstract class MessagingTransport
{
    /// <summary>Initializes a transport.</summary>
    /// <param name="name">The transport's name, used in diagnostics (for example <c>"rabbitmq"</c>).</param>
    protected MessagingTransport(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the transport's name, used in diagnostics.</summary>
    public string Name { get; }

    /// <summary>
    /// Registers the transport with MassTransit, for example with <c>UsingRabbitMq</c>. Called once by
    /// <see cref="Extensions.MessagingBusBuilder.Build"/>, inside <c>AddMassTransit</c>, after every consumer is
    /// registered.
    /// </summary>
    /// <param name="configurator">The MassTransit registration configurator.</param>
    /// <param name="settings">
    /// The builder's settings. The transport's bus callback must end by calling
    /// <see cref="MessagingTransportSettings.ConfigureBus"/>.
    /// </param>
    public abstract void Configure(IBusRegistrationConfigurator configurator, MessagingTransportSettings settings);

    /// <summary>
    /// Registers services the transport needs. Called by <see cref="Extensions.MessagingBusBuilder.Build"/>
    /// before the bus is registered. Does nothing by default.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="settings">The builder's settings.</param>
    public virtual void ConfigureServices(IServiceCollection services, MessagingTransportSettings settings)
    {
    }

    /// <summary>
    /// Applies a publish-time partition key to an outgoing message, mapped to the transport's own
    /// ordered-delivery mechanism. The default sets the routing key, which is a no-op on a transport without one.
    /// </summary>
    /// <param name="context">The outgoing send or publish context.</param>
    /// <param name="partitionKey">The partition key; never <see langword="null"/>.</param>
    public virtual void ApplyPartitionKey(SendContext context, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(context);
        RoutingKeyExtensions.TrySetRoutingKey(context, partitionKey);
    }
}
