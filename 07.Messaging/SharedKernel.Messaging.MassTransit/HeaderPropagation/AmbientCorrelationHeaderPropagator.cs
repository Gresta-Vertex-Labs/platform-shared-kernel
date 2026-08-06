using System.Diagnostics;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;

namespace SharedKernel.Messaging.MassTransit.HeaderPropagation;

/// <summary>
/// Built-in <see cref="IMessageHeaderPropagator"/> that carries distributed-trace correlation
/// identity from the ambient <see cref="Activity.Current"/> onto every outgoing message.
/// </summary>
/// <remarks>
/// <para>
/// This is one of two named, documented exceptions to "never implement
/// <see cref="IMessageHeaderPropagator"/> inside <c>SharedKernel.*</c> packages" (the general rule
/// exists because propagators normally need <em>service-specific</em> ambient context —
/// <c>IHttpContextAccessor</c>, tenant resolution — that this package cannot see). Distributed-trace
/// correlation identity needs no such dependency: <see cref="Activity.Current"/> is already ambiently
/// available to any BCL code (P-345/WO-054).
/// </para>
/// <para>
/// Register via <c>MessagingBusBuilder.WithAmbientCorrelationPropagation()</c> — a zero-argument
/// call requiring no consumer-authored class, unlike <c>MessagingBusBuilder.WithHeaderPropagator&lt;T&gt;()</c>.
/// </para>
/// </remarks>
public sealed class AmbientCorrelationHeaderPropagator : IMessageHeaderPropagator
{
    /// <summary>
    /// Sets <see cref="PublishContext.CorrelationId"/> from <see cref="Activity.Current"/>'s
    /// <see cref="Activity.TraceId"/> when an ambient <see cref="Activity"/> exists.
    /// A no-op when there is no ambient <see cref="Activity"/>.
    /// </summary>
    /// <param name="context">The <see cref="PublishContext"/> for the outgoing message.</param>
    public void Propagate(PublishContext context)
    {
        if (Activity.Current is { } activity)
            context.WithCorrelationId(Guid.Parse(activity.TraceId.ToString()));
    }
}
