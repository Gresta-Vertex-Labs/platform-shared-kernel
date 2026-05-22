namespace SharedKernel.Domain.Events;

/// <summary>
/// Abstract base record for all domain events in SharedKernel-based microservices.
/// Provides a stable <see cref="Id"/> generated at construction and an <c>init</c>-only
/// <see cref="OccurredOn"/> timestamp that must be supplied by the caller.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OccurredOn"/> must be sourced from <c>IClock.UtcNow</c> via the aggregate's
/// <c>RaiseDomainEvent(Func&lt;DateTimeOffset, IDomainEvent&gt;)</c> factory overload.
/// Direct <c>DateTimeOffset.UtcNow</c> usage anywhere in this package is a hard violation.
/// </para>
/// <para>
/// Concrete domain event records should be sealed and add only the domain-specific properties
/// they require. Use record positional parameters or <c>required init</c> properties.
/// </para>
/// <example>
/// <code>
/// public sealed record OrderPlacedEvent(Guid OrderId) : DomainEvent
/// {
///     // OccurredOn is supplied by the aggregate via RaiseDomainEvent(clock => new OrderPlacedEvent(Id) { OccurredOn = clock })
/// }
/// </code>
/// </example>
/// </remarks>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>Gets the unique identifier of this event instance. Generated automatically at construction.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets the UTC timestamp at which this event occurred.
    /// This value is <c>init</c>-only and must be supplied by the aggregate's clock-sourced raise overload.
    /// </summary>
    public required DateTimeOffset OccurredOn { get; init; }
}
