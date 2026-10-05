using Bogus;
using SharedKernel.Contracts.Events;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Abstract <see cref="Faker{T}"/> base for generating <see cref="IIntegrationEvent"/>-implementing
/// test instances.
/// </summary>
/// <typeparam name="TEvent">The concrete integration event type being faked.</typeparam>
/// <remarks>
/// Subclasses call <see cref="RuleForEventId"/> and <see cref="RuleForOccurredOn"/> in their
/// constructor, then declare their own additional <c>RuleFor(...)</c> calls for the remaining
/// event-specific fields.
/// </remarks>
public abstract class IntegrationEventFaker<TEvent> : Faker<TEvent>
    where TEvent : class, IIntegrationEvent
{
    /// <summary>Pre-wires <see cref="IIntegrationEvent.EventId"/> to <c>f.Random.Guid()</c>.</summary>
    protected void RuleForEventId() => RuleFor(e => e.EventId, f => f.Random.Guid());

    /// <summary>Pre-wires <see cref="IIntegrationEvent.OccurredOn"/> to <c>f.Date.RecentOffset()</c>.</summary>
    protected void RuleForOccurredOn() => RuleFor(e => e.OccurredOn, f => f.Date.RecentOffset());
}
