namespace SharedKernel.Domain.Events;

/// <summary>
/// Base record for domain events, supplying a stable, time-ordered <see cref="Id"/> and a required
/// <see cref="OccurredOn"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity survives serialization.</b> <see cref="Id"/> is a version 7 UUID generated when the event
/// is created, and it is <c>init</c>-settable, so a serializer restores the original value when the event
/// is read back from an outbox or a message. Deduplication keyed on <see cref="Id"/> therefore sees the
/// same event as the same event. Never assign it yourself when raising a new event.
/// </para>
/// <para>
/// <b>Time comes from the aggregate.</b> Supply <see cref="OccurredOn"/> through
/// <c>RaiseDomainEvent(at =&gt; new MyEvent { OccurredOn = at })</c>, which reads the aggregate's clock.
/// </para>
/// <para>
/// Declare concrete events as <see langword="sealed"/> records, and mark each with
/// <see cref="DomainEventVersionAttribute"/> from its first schema version.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DomainEventVersion(1)]
/// public sealed record OrderPlaced(Guid OrderId) : DomainEvent;
///
/// // Inside the Order aggregate:
/// RaiseDomainEvent(at =&gt; new OrderPlaced(Id.Value) { OccurredOn = at });
/// </code>
/// </example>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc/>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc/>
    public required DateTimeOffset OccurredOn { get; init; }
}
