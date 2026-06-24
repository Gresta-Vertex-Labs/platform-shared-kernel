using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class ProjectionSpecificationBuilderTests
{
    [Fact]
    public void Build_WithCriteriaAndSelector_AppliesBoth()
    {
        var spec = new ProjectionSpecificationBuilder<TestOrder, string>()
            .WithCriteria(o => o.Total > 100)
            .WithSelector(o => o.Customer)
            .Build();

        Assert.NotNull(spec.Criteria);
        Assert.NotNull(spec.Selector);
    }

    [Fact]
    public void Build_WithoutCriteria_LeavesCriteriaNull()
    {
        var spec = new ProjectionSpecificationBuilder<TestOrder, string>()
            .WithSelector(o => o.Customer)
            .Build();

        Assert.Null(spec.Criteria);
    }

    [Fact]
    public void Build_WithoutSelector_Throws()
    {
        var builder = new ProjectionSpecificationBuilder<TestOrder, string>().WithCriteria(o => true);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Selector_CompilesAndProjectsCorrectly()
    {
        var spec = new ProjectionSpecificationBuilder<TestOrder, string>()
            .WithSelector(o => o.Customer)
            .Build();

        var order = new TestOrder(Guid.NewGuid(), "Acme Corp", 50, new SharedKernel.Testing.Clocks.FakeClock());
        var projected = spec.Selector.Compile()(order);

        Assert.Equal("Acme Corp", projected);
    }

    [Fact]
    public void WithCriteria_NullCriteria_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ProjectionSpecificationBuilder<TestOrder, string>().WithCriteria(null!));

    [Fact]
    public void WithSelector_NullSelector_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ProjectionSpecificationBuilder<TestOrder, string>().WithSelector(null!));
}
