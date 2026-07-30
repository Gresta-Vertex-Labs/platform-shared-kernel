using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-33: P-308b/WO-051 — ISpecification&lt;T&gt;.AsSplitQuery tests, mirroring the existing
/// AsNoTracking test suite exactly.
/// </summary>
public class AsSplitQueryTests
{
    private sealed record Order(string Name, bool IsActive);

    private sealed class ActiveOrderSpec : Specification<Order>
    {
        public ActiveOrderSpec() => AddCriteria(o => o.IsActive);
    }

    private sealed class SplitQueryOrderSpec : Specification<Order>
    {
        public SplitQueryOrderSpec()
        {
            AddCriteria(o => o.IsActive);
            ApplySplitQuery();
        }
    }

    // --- Default / builder ---

    [Fact]
    public void Specification_AsSplitQuery_DefaultIsFalse()
    {
        var spec = new ActiveOrderSpec();
        spec.AsSplitQuery.Should().BeFalse();
    }

    [Fact]
    public void Specification_ApplySplitQuery_SetsTrue()
    {
        var spec = new SplitQueryOrderSpec();
        spec.AsSplitQuery.Should().BeTrue();
    }

    // --- AndSpecification propagation ---

    [Fact]
    public void AndSpecification_AsSplitQuery_TrueWhenEitherOperandIsTrue()
    {
        var split = new SplitQueryOrderSpec();
        var normal = new ActiveOrderSpec();

        var and1 = split.And(normal);
        var and2 = normal.And(split);
        var and3 = normal.And(normal);

        and1.AsSplitQuery.Should().BeTrue();
        and2.AsSplitQuery.Should().BeTrue();
        and3.AsSplitQuery.Should().BeFalse();
    }

    // --- OrSpecification propagation ---

    [Fact]
    public void OrSpecification_AsSplitQuery_TrueWhenEitherOperandIsTrue()
    {
        var split = new SplitQueryOrderSpec();
        var normal = new ActiveOrderSpec();

        var or1 = split.Or(normal);
        var or2 = normal.Or(split);
        var or3 = normal.Or(normal);

        or1.AsSplitQuery.Should().BeTrue();
        or2.AsSplitQuery.Should().BeTrue();
        or3.AsSplitQuery.Should().BeFalse();
    }

    // --- NotSpecification propagation ---

    [Fact]
    public void NotSpecification_AsSplitQuery_TrueWhenOperandIsTrue()
    {
        var split = new SplitQueryOrderSpec();
        var normal = new ActiveOrderSpec();

        var not1 = split.Not();
        var not2 = normal.Not();

        not1.AsSplitQuery.Should().BeTrue();
        not2.AsSplitQuery.Should().BeFalse();
    }
}
