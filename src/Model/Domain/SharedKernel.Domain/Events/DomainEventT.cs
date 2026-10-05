namespace SharedKernel.Domain.Events;

/// <summary>
/// Base record for a domain event whose data is carried as one strongly-typed <see cref="Payload"/>.
/// </summary>
/// <typeparam name="TPayload">
/// The type of the event data, typically a sealed record. Must be non-null.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Choose this base when the same payload is also forwarded as an integration event, or when
/// handlers treat several events through their payload type. For an event whose data fits naturally as its
/// own properties, derive from <see cref="DomainEvent"/> instead; both bases are equally supported.
/// </para>
/// <para>
/// <b>Declaration.</b> As with <see cref="DomainEvent"/>, mark the concrete event with
/// <see cref="DomainEventVersionAttribute"/>. The payload type is not versioned separately, so a breaking
/// change to it is a breaking change to the event.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderPlacedPayload(Guid OrderId, decimal Total, string Currency);
///
/// [DomainEventVersion(1)]
/// public sealed record OrderPlaced : DomainEvent&lt;OrderPlacedPayload&gt;;
///
/// // Inside the Order aggregate:
/// RaiseDomainEvent(at =&gt; new OrderPlaced
/// {
///     OccurredOn = at,
///     Payload = new OrderPlacedPayload(Id.Value, Total.Amount, Total.Currency.Code),
/// });
/// </code>
/// </example>
public abstract record DomainEvent<TPayload> : DomainEvent where TPayload : notnull
{
    /// <summary>Gets the event data; it must be set in the object initializer.</summary>
    public required TPayload Payload { get; init; }
}
