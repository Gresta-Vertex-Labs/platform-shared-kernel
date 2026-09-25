using SharedKernel.Domain.Aggregates;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

// Shared fixtures for Persistence/ self-tests — none are part of the testing-infrastructure
// public surface; they exist purely to drive ProjectionSpecificationBuilder, BulkAggregateFaker,
// and WithDeletedSpecification tests (the EF Core fixtures live in SharedKernel.Testing.Internal.Tests).

public sealed class TestOrder : AggregateRoot<Guid>
{
    public TestOrder(Guid id, string customer, decimal total, IClock clock) : base(id, clock)
    {
        Customer = customer;
        Total = total;
    }

    private TestOrder() { }

    public string Customer { get; private set; } = string.Empty;

    public decimal Total { get; private set; }
}

public sealed class TestOrderFaker : AggregateRootFaker<TestOrder, Guid>
{
    public TestOrderFaker()
    {
        CustomInstantiator(f => new TestOrder(f.Random.Guid(), f.Person.FullName, f.Random.Decimal(1, 1000), new FakeClock()));
    }
}

