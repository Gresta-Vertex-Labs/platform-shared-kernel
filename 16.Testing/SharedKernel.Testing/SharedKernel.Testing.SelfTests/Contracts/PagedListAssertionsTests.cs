using SharedKernel.Contracts.Pagination;
using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class PagedListAssertionsTests
{
    [Fact]
    public void ShouldHaveTotalCount_Matching_DoesNotThrow()
    {
        var list = PagedList<int>.Create([1, 2], page: 1, pageSize: 10, totalCount: 2);
        list.ShouldHaveTotalCount(2);
    }

    [Fact]
    public void ShouldHaveTotalCount_Mismatch_Throws()
    {
        var list = PagedList<int>.Create([1, 2], page: 1, pageSize: 10, totalCount: 2);
        Assert.Throws<InvalidOperationException>(() => list.ShouldHaveTotalCount(99));
    }

    [Fact]
    public void ShouldHaveItems_MatchingInOrder_DoesNotThrow()
    {
        var list = PagedList<int>.Create([1, 2, 3], page: 1, pageSize: 10, totalCount: 3);
        list.ShouldHaveItems(1, 2, 3);
    }

    [Fact]
    public void ShouldHaveItems_Mismatch_Throws()
    {
        var list = PagedList<int>.Create([1, 2, 3], page: 1, pageSize: 10, totalCount: 3);
        Assert.Throws<InvalidOperationException>(() => list.ShouldHaveItems(3, 2, 1));
    }

    [Fact]
    public void ShouldBeEmpty_EmptyList_DoesNotThrow()
    {
        var list = PagedList<int>.Create([], page: 1, pageSize: 10, totalCount: 0);
        list.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldBeEmpty_NonEmptyList_Throws()
    {
        var list = PagedList<int>.Create([1], page: 1, pageSize: 10, totalCount: 1);
        Assert.Throws<InvalidOperationException>(list.ShouldBeEmpty);
    }

    [Fact]
    public void ShouldHaveTotalCount_AboveIntMaxValue_Matching_DoesNotThrow()
    {
        var total = (long)int.MaxValue + 1;
        var list = PagedList<int>.Create([1], page: 1, pageSize: 10, totalCount: total);
        list.ShouldHaveTotalCount(total);
    }
}

public sealed class CursorPagedListAssertionsTests
{
    [Fact]
    public void ShouldHaveItems_MatchingInOrder_DoesNotThrow()
    {
        var list = CursorPagedList<int>.Create([1, 2, 3], nextCursor: null);
        list.ShouldHaveItems(1, 2, 3);
    }

    [Fact]
    public void ShouldHaveItems_Mismatch_Throws()
    {
        var list = CursorPagedList<int>.Create([1, 2, 3], nextCursor: null);
        Assert.Throws<InvalidOperationException>(() => list.ShouldHaveItems(3, 2, 1));
    }

    [Fact]
    public void ShouldBeEmpty_EmptyList_DoesNotThrow() =>
        CursorPagedList<int>.Empty().ShouldBeEmpty();

    [Fact]
    public void ShouldBeEmpty_NonEmptyList_Throws()
    {
        var list = CursorPagedList<int>.Create([1], nextCursor: null);
        Assert.Throws<InvalidOperationException>(list.ShouldBeEmpty);
    }

    [Fact]
    public void ShouldHaveNextPage_WithCursor_ReturnsIt()
    {
        var list = CursorPagedList<int>.Create([1], nextCursor: "v1.abc");

        Assert.Equal("v1.abc", list.ShouldHaveNextPage());
    }

    [Fact]
    public void ShouldHaveNextPage_LastPage_Throws()
    {
        var list = CursorPagedList<int>.Create([1], nextCursor: null);
        Assert.Throws<InvalidOperationException>(() => list.ShouldHaveNextPage());
    }

    [Fact]
    public void ShouldBeLastPage_LastPage_DoesNotThrow() =>
        CursorPagedList<int>.Create([1], nextCursor: null).ShouldBeLastPage();

    [Fact]
    public void ShouldBeLastPage_WithCursor_Throws()
    {
        var list = CursorPagedList<int>.Create([1], nextCursor: "v1.abc");
        Assert.Throws<InvalidOperationException>(list.ShouldBeLastPage);
    }
}
