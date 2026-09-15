namespace SharedKernel.Contracts.Events;

/// <summary>
/// Contract for an integration event: an immutable fact one service publishes for other services to consume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Integration event, not domain event.</b> A domain event is internal to the service that raises it and may
/// change with its model. An integration event is a deliberate public projection of that fact, versioned like
/// any other public API. Map the domain event to an integration event at the service boundary; never put a
/// domain event on the wire.
/// </para>
/// <para>
/// <b>Declaration rules.</b>
/// </para>
/// <list type="bullet">
///   <item><description>Declare it as a <c>sealed record</c> with get-only or init-only properties and no
///   behavior.</description></item>
///   <item><description>Mark it with <see cref="IntegrationEventAttribute"/> to give it a stable wire name and
///   version. <see cref="EventEnvelope.Wrap{TEvent}"/> refuses an event without one.</description></item>
///   <item><description>Carry primitives, strings, <see cref="Guid"/>, <see cref="DateTimeOffset"/> and other
///   records of the same kind. Never expose a domain type, such as an aggregate or a strongly-typed
///   identifier, as a property.</description></item>
///   <item><description>Change it additively. A breaking change needs a new
///   <see cref="IntegrationEventAttribute.Version"/>, published alongside the old one until every consumer has
///   moved.</description></item>
/// </list>
/// <para>
/// <b>Identity.</b> <see cref="EventId"/> is the deduplication key for idempotent consumers, and becomes the
/// CloudEvents <c>id</c> of the envelope. Reuse the originating domain event's identifier so a retried publish
/// of the same fact carries the same identifier.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [IntegrationEvent("orders.order-placed", Version = 1)]
/// public sealed record OrderPlaced(
///     Guid EventId,
///     DateTimeOffset OccurredOn,
///     Guid OrderId,
///     Guid CustomerId,
///     decimal TotalAmount,
///     string Currency) : IIntegrationEvent;
///
/// // At the boundary, from the domain event:
/// var integrationEvent = new OrderPlaced(
///     domainEvent.Id, domainEvent.OccurredOn, order.Id.Value, order.CustomerId.Value,
///     order.Total.Amount, order.Total.Currency.Code);
/// </code>
/// </example>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the unique identifier of this event occurrence, used by consumers to discard duplicates.
    /// </summary>
    /// <remarks>Must not be <see cref="Guid.Empty"/>.</remarks>
    Guid EventId { get; }

    /// <summary>Gets the time the underlying business fact occurred, not the time it was published.</summary>
    /// <remarks>Must not be <see langword="default"/>. Use UTC.</remarks>
    DateTimeOffset OccurredOn { get; }
}
