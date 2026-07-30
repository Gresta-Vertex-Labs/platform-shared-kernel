using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-34: P-309/WO-051 — IHasAggregateId&lt;TId&gt; marker interface tests.
/// </summary>
public class HasAggregateIdTests
{
    private sealed record OrderPlacedPayload(Guid OrderId, decimal Total);

    private sealed record OrderPlacedEvent(Guid AggregateId)
        : DomainEvent<OrderPlacedPayload>, IHasAggregateId<Guid>;

    private sealed record UncorrelatedEvent : DomainEvent
    {
        public string Data { get; init; } = string.Empty;
    }

    [Fact]
    public void ConcreteEvent_ImplementingMarker_IsDetectedViaPatternMatching()
    {
        var aggregateId = Guid.NewGuid();
        IDomainEvent evt = new OrderPlacedEvent(aggregateId)
        {
            OccurredOn = DateTimeOffset.UtcNow,
            Payload = new OrderPlacedPayload(aggregateId, 99.99m)
        };

        (evt is IHasAggregateId<Guid>).Should().BeTrue();
    }

    [Fact]
    public void ConcreteEvent_ImplementingMarker_ExposesExpectedAggregateId()
    {
        var aggregateId = Guid.NewGuid();
        IDomainEvent evt = new OrderPlacedEvent(aggregateId)
        {
            OccurredOn = DateTimeOffset.UtcNow,
            Payload = new OrderPlacedPayload(aggregateId, 99.99m)
        };

        var correlated = evt.Should().BeAssignableTo<IHasAggregateId<Guid>>().Subject;
        correlated.AggregateId.Should().Be(aggregateId);
    }

    [Fact]
    public void ConcreteEvent_NotImplementingMarker_PatternMatchReturnsFalse()
    {
        IDomainEvent evt = new UncorrelatedEvent { OccurredOn = DateTimeOffset.UtcNow, Data = "x" };

        (evt is IHasAggregateId<Guid>).Should().BeFalse();
    }

    [Fact]
    public void ConcreteEvent_NotImplementingMarker_AsPatternReturnsNull()
    {
        IDomainEvent evt = new UncorrelatedEvent { OccurredOn = DateTimeOffset.UtcNow, Data = "x" };

        var correlated = evt as IHasAggregateId<Guid>;

        correlated.Should().BeNull();
    }
}
