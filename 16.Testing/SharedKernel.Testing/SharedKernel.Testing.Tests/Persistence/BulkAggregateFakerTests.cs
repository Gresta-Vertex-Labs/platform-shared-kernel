using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class BulkAggregateFakerTests
{
    [Fact]
    public void Generate_ProducesRequestedCount()
    {
        var bulk = new BulkAggregateFaker<TestOrder, Guid>(new TestOrderFaker());

        var orders = bulk.Generate(25);

        Assert.Equal(25, orders.Count);
    }

    [Fact]
    public void Generate_Zero_ReturnsEmptyList()
    {
        var bulk = new BulkAggregateFaker<TestOrder, Guid>(new TestOrderFaker());

        var orders = bulk.Generate(0);

        Assert.Empty(orders);
    }

    [Fact]
    public void Generate_NegativeCount_Throws()
    {
        var bulk = new BulkAggregateFaker<TestOrder, Guid>(new TestOrderFaker());

        Assert.Throws<ArgumentOutOfRangeException>(() => bulk.Generate(-1));
    }

    [Fact]
    public void Generate_EachInstance_HasUniqueId()
    {
        var bulk = new BulkAggregateFaker<TestOrder, Guid>(new TestOrderFaker());

        var orders = bulk.Generate(10);

        Assert.Equal(10, orders.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public void Constructor_NullFaker_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new BulkAggregateFaker<TestOrder, Guid>(null!));
}
