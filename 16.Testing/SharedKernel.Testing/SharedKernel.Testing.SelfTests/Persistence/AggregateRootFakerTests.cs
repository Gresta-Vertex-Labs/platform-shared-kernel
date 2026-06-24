using SharedKernel.Domain.Aggregates;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class AggregateRootFakerTests
{
    private sealed class TestTenantedOrder : TenantedAggregateRoot<Guid>
    {
        public TestTenantedOrder(Guid id, Guid tenantId, decimal total, SharedKernel.Primitives.Clocks.IClock clock)
            : base(id, tenantId, clock) => Total = total;

        public decimal Total { get; }
    }

    private sealed class TestTenantedOrderFaker : TenantedAggregateFaker<TestTenantedOrder, Guid>
    {
        public TestTenantedOrderFaker()
        {
            CustomInstantiator(f => new TestTenantedOrder(
                f.Random.Guid(), f.Random.Guid(), f.Random.Decimal(1, 100), new FakeClock()));
        }
    }

    [Fact]
    public void AggregateRootFaker_Generate_ProducesConcreteAggregate()
    {
        var faker = new TestOrderFaker();
        var order = faker.Generate();

        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Fact]
    public void TenantedAggregateFaker_Generate_PopulatesNonEmptyTenantId()
    {
        var faker = new TestTenantedOrderFaker();
        var order = faker.Generate();

        Assert.NotEqual(Guid.Empty, order.TenantId);
    }

    [Fact]
    public void TenantedAggregateFaker_IsSubclassOfAggregateRootFaker()
    {
        Assert.IsAssignableFrom<AggregateRootFaker<TestTenantedOrder, Guid>>(new TestTenantedOrderFaker());
    }
}
