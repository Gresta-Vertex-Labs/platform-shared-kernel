using SharedKernel.Domain.Events;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Domain;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Domain;

public sealed class DomainEventAssertionsTests
{
    [Fact]
    public void ContainsEventOfType_FindsMatch_ReturnsIt()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();

        var found = aggregate.DomainEvents.ContainsEventOfType<TestCreatedEvent>();

        Assert.NotNull(found);
    }

    [Fact]
    public void ContainsEventOfType_NoMatch_Throws()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());

        Assert.Throws<InvalidOperationException>(() =>
            aggregate.DomainEvents.ContainsEventOfType<TestCreatedEvent>());
    }

    [Fact]
    public void ContainsExactly_CorrectCount_DoesNotThrow()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();
        aggregate.RaiseCreated();

        aggregate.DomainEvents.ContainsExactly<TestCreatedEvent>(2);
    }

    [Fact]
    public void ContainsExactly_WrongCount_Throws()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();

        Assert.Throws<InvalidOperationException>(() =>
            aggregate.DomainEvents.ContainsExactly<TestCreatedEvent>(2));
    }

    [Fact]
    public void HasNoEvents_Empty_DoesNotThrow()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());

        aggregate.DomainEvents.HasNoEvents();
    }

    [Fact]
    public void HasNoEvents_NonEmpty_Throws()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();

        Assert.Throws<InvalidOperationException>(() => aggregate.DomainEvents.HasNoEvents());
    }

    [Fact]
    public void HasNoEventsOfType_AbsentType_DoesNotThrow()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();

        aggregate.DomainEvents.HasNoEventsOfType<TestVersionedEvent>();
    }

    [Fact]
    public void HasNoEventsOfType_PresentType_Throws()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();

        Assert.Throws<InvalidOperationException>(() => aggregate.DomainEvents.HasNoEventsOfType<TestCreatedEvent>());
    }

    [Fact]
    public void ContainsEventWithVersion_MatchingVersion_ReturnsEvent()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseVersioned();

        var found = aggregate.DomainEvents.ContainsEventWithVersion<TestVersionedEvent>(2);

        Assert.NotNull(found);
    }

    [Fact]
    public void ContainsEventWithVersion_WrongVersion_Throws()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseVersioned();

        Assert.Throws<InvalidOperationException>(() =>
            aggregate.DomainEvents.ContainsEventWithVersion<TestVersionedEvent>(99));
    }

    [Fact]
    public void HasRaisedExactlyNEvents_CorrectTotal_DoesNotThrow()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();
        aggregate.RaiseVersioned();

        aggregate.DomainEvents.HasRaisedExactlyNEvents(2);
    }

    [Fact]
    public void HasRaisedExactlyNEvents_WrongTotal_Throws()
    {
        var aggregate = new TestAggregate(Guid.NewGuid(), new FakeClock());
        aggregate.RaiseCreated();

        Assert.Throws<InvalidOperationException>(() => aggregate.DomainEvents.HasRaisedExactlyNEvents(5));
    }
}
