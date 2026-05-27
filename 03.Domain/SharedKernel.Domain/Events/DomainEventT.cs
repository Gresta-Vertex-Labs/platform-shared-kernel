namespace SharedKernel.Domain.Events;

/// <summary>
/// Abstract base record for domain events that carry a strongly-typed payload.
/// Extends <see cref="DomainEvent"/> with a <typeparamref name="TPayload"/> property for
/// type-safe structured access to domain-specific event data.
/// </summary>
/// <typeparam name="TPayload">
/// The type of the domain-specific event payload. Must be non-null.
/// Typically a sealed record carrying all data the event needs to convey.
/// </typeparam>
/// <remarks>
/// <para>
/// Use <see cref="DomainEvent{TPayload}"/> when the event payload is also published as an
/// integration event payload (e.g., forwarded to <c>07.Messaging</c>) or when handlers
/// need type-safe structured access to the event data without casting.
/// </para>
/// <para>
/// The non-generic <see cref="DomainEvent"/> base is unchanged and not deprecated.
/// Simple events with no structured payload should continue to extend <see cref="DomainEvent"/> directly.
/// </para>
/// <example>
/// <code>
/// public sealed record OrderPlacedPayload(Guid OrderId, decimal Total);
///
/// public sealed record OrderPlacedEvent : DomainEvent&lt;OrderPlacedPayload&gt;;
///
/// // Raised inside the aggregate:
/// RaiseDomainEvent(ts => new OrderPlacedEvent
/// {
///     OccurredOn = ts,
///     Payload    = new OrderPlacedPayload(Id.Value, Total)
/// });
/// </code>
/// </example>
/// </remarks>
public abstract record DomainEvent<TPayload> : DomainEvent where TPayload : notnull
{
    /// <summary>
    /// Gets the domain-specific event payload.
    /// This property is <c>required init</c> — it must be set at object-initializer time.
    /// </summary>
    public required TPayload Payload { get; init; }
}
