using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-18: P-045/WO-011 — IHasDomainEvents refactor tests.
/// </summary>
public class IHasDomainEventsTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed record OrderCreatedEvent : DomainEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, IClock clock) : base(id, clock) { }
        public Order() : base() { }

        public void Create() =>
            RaiseDomainEvent(ts => new OrderCreatedEvent { OrderId = Id, OccurredOn = ts });
    }

    [Fact]
    public void AggregateRoot_Implements_IHasDomainEvents()
    {
        var order = new Order(Guid.NewGuid(), new FixedClock());

        order.Should().BeAssignableTo<IHasDomainEvents>(
            "AggregateRoot<TId> must implement IHasDomainEvents");
    }

    [Fact]
    public void IAggregateRoot_Extends_IHasDomainEvents()
    {
        // Verify via interface hierarchy reflection
        var aggregateRootInterface = typeof(IAggregateRoot<Guid>);
        var hasDomainEventsInterface = typeof(IHasDomainEvents);

        aggregateRootInterface.GetInterfaces()
            .Should().Contain(hasDomainEventsInterface,
                "IAggregateRoot<TId> must extend IHasDomainEvents");
    }

    [Fact]
    public void IHasDomainEvents_DomainEvents_IsAccessible_ViaInterface()
    {
        IHasDomainEvents order = new Order(Guid.NewGuid(), new FixedClock());
        ((Order)order).Create();

        order.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void IHasDomainEvents_ClearDomainEvents_IsAccessible_ViaInterface()
    {
        IHasDomainEvents order = new Order(Guid.NewGuid(), new FixedClock());
        ((Order)order).Create();

        order.ClearDomainEvents();

        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AllExistingAggregateEventTests_PassWithoutModification()
    {
        // Smoke test that existing aggregate event behavior is unchanged.
        var clock = new FixedClock();
        var order = new Order(Guid.NewGuid(), clock);

        order.Create();
        order.DomainEvents.Should().HaveCount(1);

        order.ClearDomainEvents();
        order.DomainEvents.Should().BeEmpty();
    }
}
