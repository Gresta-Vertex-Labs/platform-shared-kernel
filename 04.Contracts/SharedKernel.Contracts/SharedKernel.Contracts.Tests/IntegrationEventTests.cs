using SharedKernel.Contracts.Events;

namespace SharedKernel.Contracts.Tests;

// Concrete sealed record implementing IIntegrationEvent — used for assignability test
internal sealed record TestOrderPlacedIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

public sealed class IIntegrationEventTests
{
    [Fact]
    public void ConcreteRecord_IsAssignableToInterface()
    {
        var @event = new TestOrderPlacedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        @event.Should().BeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void EventId_IsAccessibleFromInterfaceReference()
    {
        var id = Guid.NewGuid();
        IIntegrationEvent @event = new TestOrderPlacedIntegrationEvent(id, DateTimeOffset.UtcNow);

        @event.EventId.Should().Be(id);
    }

    [Fact]
    public void OccurredOn_IsAccessibleFromInterfaceReference()
    {
        var timestamp = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        IIntegrationEvent @event = new TestOrderPlacedIntegrationEvent(Guid.NewGuid(), timestamp);

        @event.OccurredOn.Should().Be(timestamp);
    }

    [Fact]
    public void ConcreteRecord_IsSealed()
    {
        typeof(TestOrderPlacedIntegrationEvent).IsSealed.Should().BeTrue();
    }
}
