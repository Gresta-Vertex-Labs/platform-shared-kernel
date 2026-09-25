using MassTransit;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Propagation;

using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.Internal;

/// <summary>
/// Writes a <see cref="MessagingPublishContext"/> onto the transport, for every dispatch verb.
/// </summary>
/// <remarks>
/// <para>
/// One implementation because there is one contract. <c>IMessageBus</c> and <c>IEventPublisher</c>
/// each used to map the context themselves, and they drifted: the event path never wrote the tenant
/// header at all, so a tenant set before publishing an integration event reached the envelope body
/// and nothing else — leaving the consumer's <c>IRequestContext</c> with no tenant, which
/// <c>06.Persistence</c> fails closed on. It also set the transport correlation id only when the
/// caller happened to supply a custom header or a partition key. Both were found by
/// <c>samples/ShippingApi</c> running against a real broker (P-561).
/// </para>
/// <para>
/// The envelope's own <c>TenantId</c> field is not a substitute for the header: it exists inside
/// the serialized body, which a transport-level filter cannot read without deserializing a payload
/// it has no type for. The two carry the same value for different readers.
/// </para>
/// </remarks>
internal static class PublishContextPipe
{
    /// <summary>
    /// Applies correlation, tenant, caller-supplied headers and partition key to an outgoing message.
    /// </summary>
    /// <param name="pipe">The outgoing message's send context.</param>
    /// <param name="context">The publish context, after propagators and the caller's callback.</param>
    /// <param name="correlationId">
    /// The correlation id to stamp on the transport, or <see langword="null"/> to leave it unset.
    /// Passed in rather than read from <paramref name="context"/> because the two dispatch paths
    /// resolve it differently: the event publisher falls back to the ambient
    /// <see cref="System.Diagnostics.Activity"/>'s trace id, which is not always a GUID.
    /// </param>
    public static void Apply(SendContext pipe, MessagingPublishContext context, Guid? correlationId)
    {
        if (correlationId.HasValue)
            pipe.CorrelationId = correlationId.Value;

        // 01.Core's WellKnownHeaders — the same name 11.Communication, 13.ServiceDefaults and
        // 14.Presentation propagate a tenant under, so a message and an HTTP call agree.
        if (context.TenantId.HasValue)
            pipe.Headers.Set(WellKnownHeaders.TenantId, context.TenantId.Value.ToString());

        // Caller-supplied headers last, so an explicit header always wins over a derived one.
        foreach (var (key, value) in context.Headers)
            pipe.Headers.Set(key, value);

        // P-344/WO-054: maps to RabbitMQ routing-key affinity / Azure Service Bus session identity.
        pipe.ApplyPartitionKey(context.PartitionKey);
    }
}
