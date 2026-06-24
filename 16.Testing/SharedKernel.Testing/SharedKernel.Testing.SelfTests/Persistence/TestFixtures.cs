using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

// Shared fixtures for Persistence/ self-tests — none are part of the testing-infrastructure
// public surface; they exist purely to drive ProjectionSpecificationBuilder, BulkAggregateFaker,
// WithDeletedSpecification, and PersistenceTestHelpers tests.

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

public sealed class TestPersistenceDbContext : TestSharedKernelDbContext
{
    public TestPersistenceDbContext(DbContextOptions<TestPersistenceDbContext> options) : base(options) { }

    public DbSet<TestOrder> Orders => Set<TestOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestOrder>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Customer);
            b.Property(o => o.Total);
        });

        base.OnModelCreating(modelBuilder);
    }
}
