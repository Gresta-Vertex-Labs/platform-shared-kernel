using SharedKernel.Contracts.Events;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>Minimal <see cref="IIntegrationEvent"/> used as the dispatch payload across tests.</summary>
/// <remarks>
/// Routes as <see cref="EventName"/> — its <see cref="IntegrationEventAttribute"/> name — never as its class name.
/// </remarks>
[IntegrationEvent(TestOrderShippedEvent.EventName)]
internal sealed record TestOrderShippedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent
{
    /// <summary>The event's routing key.</summary>
    public const string EventName = "tests.webhooks.order-shipped";
}
