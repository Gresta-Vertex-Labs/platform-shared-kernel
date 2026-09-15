namespace SharedKernel.Domain.Events;

/// <summary>A record of something significant that happened in the domain, raised by an aggregate.</summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Derive concrete events from <see cref="DomainEvent"/> or <see cref="DomainEvent{TPayload}"/>
/// rather than implementing this interface directly. An aggregate records events with <c>RaiseDomainEvent</c>;
/// infrastructure dispatches them through <see cref="Abstractions.IDomainEventDispatcher"/> after the unit of
/// work has saved the aggregate. Handlers belong to the application layer, never to the domain.
/// </para>
/// <para>
/// <b>Versioning.</b> Mark every concrete event with <see cref="DomainEventVersionAttribute"/>.
/// </para>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier of this event occurrence, used to deduplicate delivery.</summary>
    /// <remarks>Must stay the same for the life of the event, including across serialization.</remarks>
    Guid Id { get; }

    /// <summary>Gets the UTC time the event occurred, as read from the raising aggregate's clock.</summary>
    DateTimeOffset OccurredOn { get; }
}
