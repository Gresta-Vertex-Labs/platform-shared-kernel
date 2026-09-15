namespace SharedKernel.Domain.Events;

/// <summary>A record of something significant that happened in the domain.</summary>
/// <remarks>
/// Aggregates raise domain events; infrastructure dispatches them after the unit of work commits. Handlers
/// belong to the application layer, never to the domain.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier of this event occurrence.</summary>
    /// <remarks>Stable for the life of the event, including across serialization; use it to deduplicate.</remarks>
    Guid Id { get; }

    /// <summary>Gets the UTC time at which the event occurred, read from the raising aggregate's clock.</summary>
    DateTimeOffset OccurredOn { get; }
}
