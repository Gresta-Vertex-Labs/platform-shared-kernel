using SharedKernel.Contracts.Pagination;
using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class PagedListBuilderTests
{
    [Fact]
    public void WithItems_SetsDefaultTotalCount_ToItemsCount()
    {
        var list = new PagedListBuilder<int>().WithItems([1, 2, 3]).Build();

        Assert.Equal(3L, list.TotalCount);
        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public void WithTotalCount_OverridesDefault()
    {
        var list = new PagedListBuilder<int>().WithItems([1, 2]).WithTotalCount(100).Build();

        Assert.Equal(100L, list.TotalCount);
    }

    [Fact]
    public void WithTotalCount_AboveIntMaxValue_IsHonored()
    {
        var total = (long)int.MaxValue + 10;

        var list = new PagedListBuilder<int>().WithItems([1]).WithTotalCount(total).Build();

        Assert.Equal(total, list.TotalCount);
        Assert.Equal(((total - 1) / 10) + 1, list.TotalPages);
    }

    [Fact]
    public void Build_DefaultsPageAndPageSize()
    {
        var list = new PagedListBuilder<int>().WithItems([1]).Build();

        Assert.Equal(1, list.Page);
        Assert.Equal(10, list.PageSize);
    }

    [Fact]
    public void WithPage_AndWithPageSize_AreHonored()
    {
        var list = new PagedListBuilder<int>().WithItems([1]).WithPage(3).WithPageSize(25).Build();

        Assert.Equal(3, list.Page);
        Assert.Equal(25, list.PageSize);
    }

    [Fact]
    public void WithRequest_TakesPageAndPageSizeFromTheRequest()
    {
        var request = PageRequest.Create(page: 4, pageSize: 5).Value;

        var list = new PagedListBuilder<int>().WithItems([1, 2]).WithRequest(request).WithTotalCount(40).Build();

        Assert.Equal(4, list.Page);
        Assert.Equal(5, list.PageSize);
        Assert.True(list.HasNextPage);
    }

    [Fact]
    public void Build_MoreItemsThanPageSize_Throws()
    {
        var builder = new PagedListBuilder<int>().WithItems([1, 2, 3]).WithPageSize(2);

        Assert.Throws<ArgumentException>(builder.Build);
    }

    [Fact]
    public void Empty_ProducesZeroItemsAndZeroTotalCount()
    {
        var list = PagedListBuilder<int>.Empty();

        Assert.Empty(list.Items);
        Assert.Equal(0L, list.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(10, list.PageSize);
    }

    [Fact]
    public void WithItems_NullItems_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new PagedListBuilder<int>().WithItems(null!));
}
