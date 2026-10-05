using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-28 — ISpecification&lt;T&gt;.IncludeDeleted flag tests (P-095/WO-016).
/// </summary>
public class IncludeDeletedTests
{
    // --- Test double domain type ---

    private sealed record Item(int Id, bool IsDeleted);

    // --- Concrete specification implementations ---

    private sealed class DefaultSpec : Specification<Item>
    {
        public DefaultSpec() { }
    }

    private sealed class SoftDeletedSpec : Specification<Item>
    {
        public SoftDeletedSpec() => IncludeSoftDeleted();
    }

    private sealed class CriteriaSpec : Specification<Item>
    {
        public CriteriaSpec(bool includeDeleted)
        {
            AddCriteria(i => i.Id > 0);
            if (includeDeleted)
                IncludeSoftDeleted();
        }
    }

    // --- (1) Default IncludeDeleted is false ---

    [Fact]
    public void DefaultSpec_IncludeDeleted_IsFalse()
    {
        var spec = new DefaultSpec();
        spec.IncludeDeleted.Should().BeFalse();
    }

    // --- (2) IncludeSoftDeleted() sets IncludeDeleted to true ---

    [Fact]
    public void SoftDeletedSpec_IncludeDeleted_IsTrue()
    {
        var spec = new SoftDeletedSpec();
        spec.IncludeDeleted.Should().BeTrue();
    }

    // --- (3) AndSpecification: left true, right false → composed true ---

    [Fact]
    public void And_LeftIncludeDeleted_True_RightFalse_ComposedIsTrue()
    {
        var left = new SoftDeletedSpec();   // IncludeDeleted = true
        var right = new DefaultSpec();      // IncludeDeleted = false
        var and = new AndSpecification<Item>(left, right);

        and.IncludeDeleted.Should().BeTrue();
    }

    // --- (4) AndSpecification: both false → composed false ---

    [Fact]
    public void And_BothIncludeDeletedFalse_ComposedIsFalse()
    {
        var left = new DefaultSpec();
        var right = new DefaultSpec();
        var and = new AndSpecification<Item>(left, right);

        and.IncludeDeleted.Should().BeFalse();
    }

    // --- (5) OrSpecification: left true, right false → composed true ---

    [Fact]
    public void Or_LeftIncludeDeleted_True_RightFalse_ComposedIsTrue()
    {
        var left = new SoftDeletedSpec();   // IncludeDeleted = true
        var right = new DefaultSpec();      // IncludeDeleted = false
        var or = new OrSpecification<Item>(left, right);

        or.IncludeDeleted.Should().BeTrue();
    }

    // --- (5) OrSpecification: both false → composed false ---

    [Fact]
    public void Or_BothIncludeDeletedFalse_ComposedIsFalse()
    {
        var left = new DefaultSpec();
        var right = new DefaultSpec();
        var or = new OrSpecification<Item>(left, right);

        or.IncludeDeleted.Should().BeFalse();
    }

    // --- (6) NotSpecification: operand true → composed true ---

    [Fact]
    public void Not_OperandIncludeDeletedTrue_ComposedIsTrue()
    {
        var inner = new SoftDeletedSpec();  // IncludeDeleted = true
        var not = new NotSpecification<Item>(inner);

        not.IncludeDeleted.Should().BeTrue();
    }

    // --- (6) NotSpecification: operand false → composed false ---

    [Fact]
    public void Not_OperandIncludeDeletedFalse_ComposedIsFalse()
    {
        var inner = new DefaultSpec();
        var not = new NotSpecification<Item>(inner);

        not.IncludeDeleted.Should().BeFalse();
    }

    // --- (7) And: right true, left false → composed true (symmetric) ---

    [Fact]
    public void And_RightIncludeDeleted_True_LeftFalse_ComposedIsTrue()
    {
        var left = new DefaultSpec();       // IncludeDeleted = false
        var right = new SoftDeletedSpec();  // IncludeDeleted = true
        var and = new AndSpecification<Item>(left, right);

        and.IncludeDeleted.Should().BeTrue();
    }

    // --- (7) Or: right true, left false → composed true (symmetric) ---

    [Fact]
    public void Or_RightIncludeDeleted_True_LeftFalse_ComposedIsTrue()
    {
        var left = new DefaultSpec();       // IncludeDeleted = false
        var right = new SoftDeletedSpec();  // IncludeDeleted = true
        var or = new OrSpecification<Item>(left, right);

        or.IncludeDeleted.Should().BeTrue();
    }

    // --- Regression: all existing IncludeDeleted = false tests still pass when combined with criteria ---

    [Fact]
    public void CriteriaSpec_WithoutIncludeSoftDeleted_DefaultsFalse()
    {
        var spec = new CriteriaSpec(includeDeleted: false);
        spec.IncludeDeleted.Should().BeFalse();
    }

    [Fact]
    public void CriteriaSpec_WithIncludeSoftDeleted_IsTrue()
    {
        var spec = new CriteriaSpec(includeDeleted: true);
        spec.IncludeDeleted.Should().BeTrue();
    }
}
