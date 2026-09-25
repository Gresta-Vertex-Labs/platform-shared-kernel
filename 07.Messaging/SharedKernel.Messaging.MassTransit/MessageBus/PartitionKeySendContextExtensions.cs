using MassTransit;
using SharedKernel.Messaging.MassTransit.Transports;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// Maps <see cref="SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext.PartitionKey"/>
/// onto the configured transport's native ordered-delivery mechanism (P-344/WO-054).
/// </summary>
internal static class PartitionKeySendContextExtensions
{
    /// <summary>
    /// Applies <paramref name="partitionKey"/> to the outgoing <paramref name="context"/> through
    /// <paramref name="transport"/>. A no-op when <paramref name="partitionKey"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="context">The outgoing send/publish context.</param>
    /// <param name="partitionKey">The partition key, or <see langword="null"/> when unset.</param>
    /// <param name="transport">
    /// The transport <c>MessagingBusBuilder</c> configured, or <see langword="null"/> when the bus was composed
    /// without it (tests, hand-wired hosts) — then only the routing key is set, which never throws on a transport
    /// without one.
    /// </param>
    /// <remarks>
    /// <para>
    /// <strong>RabbitMQ</strong>: <see cref="RoutingKeyExtensions.TrySetRoutingKey"/> drives routing-key
    /// affinity so all messages sharing a key traverse the same queue-binding path in publish order.
    /// </para>
    /// <para>
    /// <strong>Azure Service Bus</strong>: its transport package also sets the session identifier. The
    /// receiving endpoint must have sessions enabled for the ordering guarantee to hold.
    /// </para>
    /// <para>
    /// The mapping lives on <see cref="MessagingTransport.ApplyPartitionKey"/> because the core package
    /// references no broker client (P-570).
    /// </para>
    /// </remarks>
    public static void ApplyPartitionKey(this SendContext context, string? partitionKey, MessagingTransport? transport = null)
    {
        if (partitionKey is null)
            return;

        if (transport is null)
            RoutingKeyExtensions.TrySetRoutingKey(context, partitionKey);
        else
            transport.ApplyPartitionKey(context, partitionKey);
    }
}
