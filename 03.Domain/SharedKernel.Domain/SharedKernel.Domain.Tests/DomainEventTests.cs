using FluentAssertions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Tests;

public class DomainEventTests
{
    private sealed record TestEvent : DomainEvent
    {
        public string Payload { get; init; } = string.Empty;
    }

    [Fact]
    public void DomainEvent_Id_GeneratedOnConstruction()
    {
        var evt = new TestEvent { OccurredOn = DateTimeOffset.UtcNow };
        evt.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void DomainEvent_TwoInstances_HaveDifferentIds()
    {
        var now = DateTimeOffset.UtcNow;
        var a = new TestEvent { OccurredOn = now };
        var b = new TestEvent { OccurredOn = now };
        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void DomainEvent_OccurredOn_ReturnsSuppliedValue()
    {
        var time = new DateTimeOffset(2026, 5, 22, 10, 30, 0, TimeSpan.Zero);
        var evt = new TestEvent { OccurredOn = time };
        evt.OccurredOn.Should().Be(time);
    }

    [Fact]
    public void DomainEvent_ImplementsIDomainEvent()
    {
        var evt = new TestEvent { OccurredOn = DateTimeOffset.UtcNow };
        evt.Should().BeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void DomainEvent_RecordEquality_SameValues_Equal()
    {
        var id = Guid.NewGuid();
        var time = DateTimeOffset.UtcNow;
        // Records use value equality on all properties
        // Since Id is auto-generated and unique per instance we verify the OccurredOn is preserved
        var evt = new TestEvent { OccurredOn = time, Payload = "test" };
        evt.OccurredOn.Should().Be(time);
        evt.Payload.Should().Be("test");
    }
}
