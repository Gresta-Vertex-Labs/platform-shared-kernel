using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Specifications;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Testing.SelfTests.Domain;

// Shared fixtures used across Domain/ self-tests — kept in one file since none of these
// types are part of the public testing-infrastructure surface; they exist purely to drive it.

public sealed record TestCreatedEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

[DomainEventVersion(2)]
public sealed record TestVersionedEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

public sealed record TestUnversionedEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

public sealed class TestAggregate : AggregateRoot<Guid>
{
    public TestAggregate(Guid id, IClock clock) : base(id, clock) { }

    public void RaiseCreated() => RaiseDomainEvent(ts => new TestCreatedEvent(Guid.NewGuid(), ts));

    public void RaiseVersioned() => RaiseDomainEvent(ts => new TestVersionedEvent(Guid.NewGuid(), ts));
}

public sealed class AlwaysBrokenRule : IBusinessRule
{
    public string Code => "test.rule";
    public string Message => "Always broken.";

    public bool IsBroken() => true;
}

public sealed class NeverBrokenRule : IBusinessRule
{
    public string Code => "test.rule";
    public string Message => "Never broken.";

    public bool IsBroken() => false;
}

public sealed record TestSpecEntity(int Value);

public sealed class TestSpecEntitySpec : Specification<TestSpecEntity>
{
    public TestSpecEntitySpec(int threshold) => AddCriteria(e => e.Value > threshold);
}

public sealed class AllMatchSpec : Specification<TestSpecEntity>
{
    // Criteria left null — matches everything.
}
