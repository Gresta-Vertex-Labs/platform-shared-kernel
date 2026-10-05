namespace SharedKernel.Domain.Events;

/// <summary>
/// Base record for a domain event, supplying a time-ordered <see cref="Id"/> that survives serialization and
/// a required <see cref="OccurredOn"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity.</b> <see cref="Id"/> is a version 7 UUID generated when the event is constructed. It is
/// <c>init</c>-settable so a serializer restores the original value when the event is read back from an
/// outbox or a message, and deduplication keyed on <see cref="Id"/> recognizes the same occurrence. Never
/// assign it when raising a new event.
/// </para>
/// <para>
/// <b>Time.</b> Supply <see cref="OccurredOn"/> through the factory overload of <c>RaiseDomainEvent</c>,
/// which passes the aggregate clock's current UTC time to the factory (see the example). Never read
/// <see cref="DateTimeOffset.UtcNow"/> directly.
/// </para>
/// <para>
/// <b>Declaration.</b> Declare each concrete event as a <see langword="sealed"/> record and mark it with
/// <see cref="DomainEventVersionAttribute"/>, starting at version 1.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DomainEventVersion(1)]
/// public sealed record OrderPlaced(OrderId OrderId, Money Total) : DomainEvent;
///
/// // Inside the Order aggregate:
/// RaiseDomainEvent(at =&gt; new OrderPlaced(Id, Total) { OccurredOn = at });
/// </code>
/// </example>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>Gets the unique, time-ordered identifier of this event occurrence.</summary>
    /// <remarks>
    /// Defaults to a new version 7 UUID. Set it only when rehydrating a stored or received event.
    /// </remarks>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc/>
    public required DateTimeOffset OccurredOn { get; init; }
}
