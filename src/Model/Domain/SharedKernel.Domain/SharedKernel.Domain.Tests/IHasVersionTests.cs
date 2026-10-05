using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-21: P-048/WO-011 — IHasVersion version counter tests.
/// </summary>
public class IHasVersionTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed record SomethingHappened : DomainEvent
    {
        public Guid Id2 { get; init; }
    }

    private sealed class Counter : AggregateRoot<Guid>
    {
        public Counter(Guid id, IClock clock) : base(id, clock) { }
        public Counter() : base() { }

        public void DoSomething() =>
            RaiseDomainEvent(ts => new SomethingHappened { Id2 = Id, OccurredOn = ts });

        public void DoSomethingPreBuilt(IDomainEvent evt) =>
            RaiseDomainEvent(evt);
    }

    [Fact]
    public void AggregateRoot_InitialVersion_IsZero()
    {
        var counter = new Counter(Guid.NewGuid(), new FixedClock());

        counter.Version.Should().Be(0);
    }

    [Fact]
    public void AggregateRoot_Raising3Events_Version_IsThree()
    {
        var counter = new Counter(Guid.NewGuid(), new FixedClock());

        counter.DoSomething();
        counter.DoSomething();
        counter.DoSomething();

        counter.Version.Should().Be(3);
    }

    [Fact]
    public void AggregateRoot_ClearDomainEvents_DoesNotDecrementVersion()
    {
        var counter = new Counter(Guid.NewGuid(), new FixedClock());

        counter.DoSomething();
        counter.DoSomething();
        counter.ClearDomainEvents();

        counter.Version.Should().Be(2, "ClearDomainEvents must not decrement Version");
    }

    [Fact]
    public void AggregateRoot_RaiseDomainEvent_PreBuilt_IncrementsVersion()
    {
        var clock = new FixedClock();
        var counter = new Counter(Guid.NewGuid(), clock);
        var evt = new SomethingHappened { Id2 = Guid.NewGuid(), OccurredOn = clock.UtcNow };

        counter.DoSomethingPreBuilt(evt);

        counter.Version.Should().Be(1);
    }

    [Fact]
    public void AggregateRoot_Implements_IHasVersion()
    {
        var counter = new Counter(Guid.NewGuid(), new FixedClock());

        counter.Should().BeAssignableTo<IHasVersion>();
    }
}
