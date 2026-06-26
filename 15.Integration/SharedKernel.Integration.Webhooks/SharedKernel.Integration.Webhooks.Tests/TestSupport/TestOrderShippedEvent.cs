using SharedKernel.Contracts.Events;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>Minimal <see cref="IIntegrationEvent"/> used as the dispatch payload across tests.</summary>
internal sealed record TestOrderShippedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent;
