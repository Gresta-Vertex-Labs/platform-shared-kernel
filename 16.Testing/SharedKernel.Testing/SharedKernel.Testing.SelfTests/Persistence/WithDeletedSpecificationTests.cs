using SharedKernel.Domain.Specifications;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class WithDeletedSpecificationTests
{
    private sealed class OrdersOverThresholdSpec : Specification<TestOrder>
    {
        public OrdersOverThresholdSpec(decimal threshold)
        {
            AddCriteria(o => o.Total > threshold);
            ApplyOrderByDescending(o => o.Total);
            ApplyPaging(0, 10);
        }
    }

    [Fact]
    public void Wrap_SetsIncludeDeletedTrue()
    {
        var inner = new OrdersOverThresholdSpec(100);
        var wrapped = WithDeletedSpecification<TestOrder>.Wrap(inner);

        Assert.True(wrapped.IncludeDeleted);
    }

    [Fact]
    public void Wrap_OriginalSpecification_IsUntouched()
    {
        var inner = new OrdersOverThresholdSpec(100);
        WithDeletedSpecification<TestOrder>.Wrap(inner);

        Assert.False(inner.IncludeDeleted);
    }

    [Fact]
    public void Wrap_PreservesCriteriaAndOrderingAndPaging()
    {
        var inner = new OrdersOverThresholdSpec(100);
        var wrapped = WithDeletedSpecification<TestOrder>.Wrap(inner);

        Assert.NotNull(wrapped.Criteria);
        Assert.NotNull(wrapped.OrderByDescending);
        Assert.Equal(0, wrapped.Skip);
        Assert.Equal(10, wrapped.Take);
    }

    [Fact]
    public void Wrap_NullInner_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WithDeletedSpecification<TestOrder>.Wrap(null!));
}
