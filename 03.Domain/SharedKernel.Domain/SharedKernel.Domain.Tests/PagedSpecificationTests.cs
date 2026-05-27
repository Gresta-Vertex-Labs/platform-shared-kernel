using FluentAssertions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Domain.Tests;

/// <summary>
/// T-24: P-051/WO-011 — PagedSpecification&lt;T&gt; tests.
/// </summary>
public class PagedSpecificationTests
{
    private sealed record Item(int Id, string Name);

    private sealed class AllItemsPagedSpec : PagedSpecification<Item>
    {
        public AllItemsPagedSpec(int page, int pageSize) : base(page, pageSize) { }
    }

    [Fact]
    public void PagedSpecification_Page1_PageSize10_SkipZero_Take10()
    {
        var spec = new AllItemsPagedSpec(1, 10);

        spec.Skip.Should().Be(0);
        spec.Take.Should().Be(10);
    }

    [Fact]
    public void PagedSpecification_Page3_PageSize20_Skip40_Take20()
    {
        var spec = new AllItemsPagedSpec(3, 20);

        spec.Skip.Should().Be(40);
        spec.Take.Should().Be(20);
    }

    [Fact]
    public void PagedSpecification_Page_IsReadable()
    {
        var spec = new AllItemsPagedSpec(5, 10);
        spec.Page.Should().Be(5);
    }

    [Fact]
    public void PagedSpecification_PageSize_IsReadable()
    {
        var spec = new AllItemsPagedSpec(1, 25);
        spec.PageSize.Should().Be(25);
    }

    [Fact]
    public void PagedSpecification_AsNoTracking_IsTrue()
    {
        var spec = new AllItemsPagedSpec(1, 10);
        spec.AsNoTracking.Should().BeTrue("PagedSpecification extends ReadOnlySpecification");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void PagedSpecification_PageLessThan1_ThrowsArgumentOutOfRangeException(int page)
    {
        var act = () => new AllItemsPagedSpec(page, 10);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PagedSpecification_PageSizeLessThan1_ThrowsArgumentOutOfRangeException(int pageSize)
    {
        var act = () => new AllItemsPagedSpec(1, pageSize);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("pageSize");
    }

    [Fact]
    public void PagedSpecification_PageSizeExceedsMaxPageSize_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new AllItemsPagedSpec(1, 1001);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("pageSize");
    }

    [Fact]
    public void PagedSpecification_PageSizeAtMaxPageSize_DoesNotThrow()
    {
        var act = () => new AllItemsPagedSpec(1, 1000);
        act.Should().NotThrow();
    }
}
