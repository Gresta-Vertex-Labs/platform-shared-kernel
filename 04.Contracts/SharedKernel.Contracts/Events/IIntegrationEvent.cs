namespace SharedKernel.Contracts.Events;

/// <summary>
/// Marker interface for integration event payloads — the public contract projection of a domain event.
/// </summary>
/// <remarks>
/// <para>
/// Integration events are immutable DTOs that represent a fact that occurred in one service and
/// must be communicated to other services. They are the public API surface of domain events.
/// </para>
/// <para>
/// <strong>Implementation contract:</strong>
/// <list type="bullet">
/// <item><description>Implementations must be <c>sealed record</c> or <c>sealed class</c>.</description></item>
/// <item><description>They must be immutable — use <c>init</c>-only or <c>get</c>-only properties.</description></item>
/// <item><description>They must carry no behavior and no domain logic.</description></item>
/// <item><description>Consumers must never cast an <see cref="IIntegrationEvent"/> back to a domain type.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>EventId traceability:</strong> <see cref="EventId"/> maps to <c>IDomainEvent.Id</c> from the
/// originating domain event in <c>03.Domain</c>. This preserves a direct trace from the integration
/// event on the wire back to the domain event that caused it.
/// </para>
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the unique identifier of this event.
    /// Maps to <c>IDomainEvent.Id</c> from the originating domain event.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets the UTC timestamp at which the originating domain event occurred.
    /// </summary>
    DateTimeOffset OccurredOn { get; }
}
