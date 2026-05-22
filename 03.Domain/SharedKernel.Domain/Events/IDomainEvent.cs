namespace SharedKernel.Domain.Events;

/// <summary>
/// Represents a domain event — a record of something significant that occurred within the domain.
/// </summary>
/// <remarks>
/// Domain events are raised by aggregate roots and dispatched by infrastructure after the unit of
/// work commits successfully. Handlers for domain events live in <c>05.Application</c>, never here.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier of this event instance.</summary>
    Guid Id { get; }

    /// <summary>
    /// Gets the UTC timestamp at which this event occurred.
    /// Must be sourced from <c>IClock.UtcNow</c> via the aggregate's
    /// <c>RaiseDomainEvent(Func&lt;DateTimeOffset, IDomainEvent&gt;)</c> overload.
    /// Direct use of <c>DateTimeOffset.UtcNow</c> is a hard violation.
    /// </summary>
    DateTimeOffset OccurredOn { get; }
}
