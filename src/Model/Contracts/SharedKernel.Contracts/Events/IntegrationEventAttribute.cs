namespace SharedKernel.Contracts.Events;

/// <summary>
/// Declares the stable wire name and schema version of an <see cref="IIntegrationEvent"/>.
/// </summary>
/// <param name="name">
/// The event's wire name, which becomes the CloudEvents <c>type</c>. See <see cref="Name"/> for the format.
/// </param>
/// <remarks>
/// <para>
/// <b>Why it is required.</b> The wire name is what brokers route on, subscriptions filter on and consumers
/// branch on. Deriving it from the class name would let a rename or a namespace move silently break every
/// consumer, so the name is declared once and never inferred.
/// </para>
/// <para>
/// <b>Validation.</b> The attribute itself accepts any value. <see cref="IntegrationEventDescriptor.For{TEvent}"/>
/// validates it the first time the event type is used and throws <see cref="InvalidOperationException"/> naming
/// the offending type.
/// </para>
/// <para>
/// <b>Pitfall.</b> The attribute is not inherited. Every concrete event type declares its own.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [IntegrationEvent("orders.order-placed", Version = 2)]
/// public sealed record OrderPlacedV2(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IntegrationEventAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the event's wire name: 1 to 128 lowercase ASCII letters and digits, in segments separated by a single
    /// <c>.</c>, <c>-</c> or <c>_</c>, such as <c>orders.order-placed</c>.
    /// </summary>
    /// <remarks>
    /// Prefix it with the owning bounded context so names stay unique across services. Never change it once
    /// shipped; publish a new event instead.
    /// </remarks>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or initializes the schema version of the event's data, starting at 1. Defaults to 1.
    /// </summary>
    /// <remarks>
    /// Increment it for a breaking change to the event's shape, and keep the name. It travels as the
    /// <c>dataversion</c> CloudEvents extension attribute so a consumer can branch before it reads the data.
    /// </remarks>
    public int Version { get; init; } = 1;
}
