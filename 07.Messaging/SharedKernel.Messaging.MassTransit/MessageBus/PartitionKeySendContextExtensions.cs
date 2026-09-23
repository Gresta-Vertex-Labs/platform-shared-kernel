using MassTransit;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// Maps <see cref="SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext.PartitionKey"/>
/// onto each transport's genuine native ordered-delivery mechanism (P-344/WO-054).
/// </summary>
internal static class PartitionKeySendContextExtensions
{
    /// <summary>
    /// Applies <paramref name="partitionKey"/> as both a RabbitMQ routing key and an Azure Service Bus
    /// session identifier on the outgoing <paramref name="context"/>. A no-op when
    /// <paramref name="partitionKey"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="context">The outgoing send/publish context.</param>
    /// <param name="partitionKey">The partition key, or <see langword="null"/> when unset.</param>
    /// <remarks>
    /// <para>
    /// Both underlying MassTransit calls are applied unconditionally, independent of which transport
    /// is actually configured — neither throws when the corresponding transport-specific send-context
    /// payload is unavailable (confirmed empirically against MassTransit's RabbitMQ, Azure
    /// Service Bus, and in-memory transports: <see cref="RoutingKeyExtensions.TrySetRoutingKey"/>
    /// never throws, and <see cref="ServiceBusSendContextExtensions.SetSessionId"/> is a silent no-op
    /// when the <see cref="ServiceBusSendContext"/> payload is not present). This lets
    /// <see cref="SharedKernel.Messaging.MassTransit.MessageBus.MassTransitMessageBus"/> and
    /// <see cref="SharedKernel.Messaging.MassTransit.EventPublisher.MassTransitEventPublisher"/> apply
    /// the mapping identically without branching on which transport
    /// <see cref="SharedKernel.Messaging.MassTransit.Extensions.MessagingBusBuilder"/> configured.
    /// </para>
    /// <para>
    /// <strong>RabbitMQ</strong>: <see cref="RoutingKeyExtensions.TrySetRoutingKey"/> drives routing-key
    /// affinity so all messages sharing a key traverse the same queue-binding path in publish order.
    /// </para>
    /// <para>
    /// <strong>Azure Service Bus</strong>: <see cref="ServiceBusSendContextExtensions.SetSessionId"/>
    /// applies the outgoing message's session identifier. The receiving endpoint must have sessions
    /// enabled for the ordering guarantee to hold — enabling sessions on an endpoint is a
    /// consuming-service/infrastructure responsibility this package does not silently apply
    /// retroactively.
    /// </para>
    /// </remarks>
    public static void ApplyPartitionKey(this SendContext context, string? partitionKey)
    {
        if (partitionKey is null)
            return;

        RoutingKeyExtensions.TrySetRoutingKey(context, partitionKey);
        ServiceBusSendContextExtensions.SetSessionId(context, partitionKey);
    }
}
