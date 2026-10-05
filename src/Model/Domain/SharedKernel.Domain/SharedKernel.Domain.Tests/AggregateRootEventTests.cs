using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Exceptions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

public class AggregateRootEventTests
{
    // --- Test doubles ---

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
        public DateOnly Today => DateOnly.FromDateTime(_now.UtcDateTime);
    }

    private sealed record OrderCreatedEvent : DomainEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed record OrderShippedEvent : DomainEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id, IClock clock) : base(id, clock) { }
        public Order() : base() { } // ORM path

        public void Create()
        {
            RaiseDomainEvent(now => new OrderCreatedEvent
            {
                OrderId = Id,
                OccurredOn = now
            });
        }

        public void Ship()
        {
            RaiseDomainEvent(now => new OrderShippedEvent
            {
                OrderId = Id,
                OccurredOn = now
            });
        }

        public void CreatePreBuilt(IDomainEvent evt) => RaiseDomainEvent(evt);

        public void RunCheckRule(IBusinessRule rule) => CheckRule(rule);
    }

    private sealed class BrokenRule : IBusinessRule
    {
        public string Code => "test.rule";
        public string Message => "rule is broken";
        public bool IsBroken() => true;
    }

    private sealed class ValidRule : IBusinessRule
    {
        public string Code => "test.rule";
        public string Message => "rule is fine";
        public bool IsBroken() => false;
    }

    // --- Event accumulation ---

    [Fact]
    public void RaiseDomainEvent_Factory_AppearsInDomainEvents()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);

        order.Create();

        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents.First().Should().BeOfType<OrderCreatedEvent>();
    }

    [Fact]
    public void RaiseDomainEvent_Factory_SetsOccurredOnFromClock()
    {
        var fixedTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(fixedTime);
        var order = new Order(Guid.NewGuid(), clock);

        order.Create();

        var evt = order.DomainEvents.OfType<OrderCreatedEvent>().Single();
        evt.OccurredOn.Should().Be(fixedTime);
    }

    [Fact]
    public void RaiseDomainEvent_PreBuilt_AppearsInDomainEvents()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);
        var evt = new OrderCreatedEvent { OrderId = order.Id, OccurredOn = clock.UtcNow };

        order.CreatePreBuilt(evt);

        order.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(evt);
    }

    [Fact]
    public void MultipleEvents_AccumulateInOrder()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);

        order.Create();
        order.Ship();

        var events = order.DomainEvents.ToList();
        events.Should().HaveCount(2);
        events[0].Should().BeOfType<OrderCreatedEvent>();
        events[1].Should().BeOfType<OrderShippedEvent>();
    }

    // --- ClearDomainEvents ---

    [Fact]
    public void ClearDomainEvents_EmptiesCollection()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);
        order.Create();

        order.ClearDomainEvents();

        order.DomainEvents.Should().BeEmpty();
    }

    // --- Events do not leak across instances ---

    [Fact]
    public void Events_DoNotLeakAcrossInstances()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order1 = new Order(Guid.NewGuid(), clock);
        var order2 = new Order(Guid.NewGuid(), clock);

        order1.Create();

        order2.DomainEvents.Should().BeEmpty();
    }

    // --- ORM-path constructor uses NullClock ---

    [Fact]
    public void OrmPath_Constructor_HasEmptyDomainEvents()
    {
        var order = new Order();
        order.DomainEvents.Should().BeEmpty();
    }

    // --- NullClock sentinel (T-03) ---

    [Fact]
    public void OrmPath_WithoutAttachedClock_RaisingATimestampedEvent_Throws()
    {
        // Before: the ORM constructor used a NullClock and the event was silently stamped 01.01.0001.
        var order = new Order();

        var act = order.Create;

        act.Should().Throw<InvalidOperationException>().WithMessage("*AttachClock*");
        order.DomainEvents.Should().BeEmpty();
        order.Version.Should().Be(0);
    }

    [Fact]
    public void OrmPath_AfterAttachClock_StampsEventsFromTheAttachedClock()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var order = new Order();

        ((IHasClock)order).IsClockAttached.Should().BeFalse();
        ((IHasClock)order).AttachClock(clock);
        order.Create();

        ((IHasClock)order).IsClockAttached.Should().BeTrue();
        order.DomainEvents.OfType<OrderCreatedEvent>().Single().OccurredOn.Should().Be(clock.UtcNow);
    }

    // --- CheckRule ---

    [Fact]
    public void CheckRule_BrokenRule_ThrowsBusinessRuleViolationException()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);

        var act = () => order.RunCheckRule(new BrokenRule());

        act.Should().Throw<BusinessRuleViolationException>()
            .Which.Rule.Should().BeOfType<BrokenRule>();
    }

    [Fact]
    public void CheckRule_ValidRule_DoesNotThrow()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);

        var act = () => order.RunCheckRule(new ValidRule());

        act.Should().NotThrow();
    }

    [Fact]
    public void CheckRule_Exception_CarriesRuleInstance()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var order = new Order(Guid.NewGuid(), clock);
        var rule = new BrokenRule();

        var act = () => order.RunCheckRule(rule);

        act.Should().Throw<BusinessRuleViolationException>()
            .Which.Rule.Should().BeSameAs(rule);
    }
}
