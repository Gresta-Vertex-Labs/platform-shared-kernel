using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class PagedListBuilderTests
{
    [Fact]
    public void WithItems_SetsDefaultTotalCount_ToItemsCount()
    {
        var list = new PagedListBuilder<int>().WithItems([1, 2, 3]).Build();

        Assert.Equal(3, list.TotalCount);
        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public void WithTotalCount_OverridesDefault()
    {
        var list = new PagedListBuilder<int>().WithItems([1, 2]).WithTotalCount(100).Build();

        Assert.Equal(100, list.TotalCount);
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
    public void Empty_ProducesZeroItemsAndZeroTotalCount()
    {
        var list = PagedListBuilder<int>.Empty();

        Assert.Empty(list.Items);
        Assert.Equal(0, list.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(10, list.PageSize);
    }

    [Fact]
    public void WithItems_NullItems_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new PagedListBuilder<int>().WithItems(null!));
}
