using HotChocolate.Types.Pagination;
using SharedKernel.Communication.GraphQL.Pagination;

namespace SharedKernel.Communication.GraphQL.Tests;

public sealed class PagedResponseTypeTests
{
    [Fact]
    public void From_SetsItemsAndTotalCount()
    {
        var items = new List<string> { "a", "b", "c" };
        var result = PagedResponseType<string>.From(items, 42);

        result.TotalCount.Should().Be(42);
        result.Items.Should().BeEquivalentTo(items);
    }

    [Fact]
    public void DefaultInstance_HasEmptyItemsAndZeroTotalCount()
    {
        var result = new PagedResponseType<int>();
        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public void FromPage_ThrowsArgumentNull_WhenPageIsNull()
    {
        var act = () => PagedResponseType<string>.FromPage(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromConnection_ThrowsArgumentNull_WhenConnectionIsNull()
    {
        var act = () => PagedResponseType<string>.FromConnection(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromConnection_BuildsCorrectShape()
    {
        // Build a Connection<string> using IndexEdge factory
        var edges = new IEdge<string>[]
        {
            IndexEdge<string>.Create("hello", 0),
            IndexEdge<string>.Create("world", 1),
        };
        var pageInfo = new ConnectionPageInfo(
            hasNextPage: false,
            hasPreviousPage: false,
            startCursor: null,
            endCursor: null);
        var connection = new Connection<string>(edges, pageInfo, totalCount: 2);

        var result = PagedResponseType<string>.FromConnection(connection);

        result.TotalCount.Should().Be(2);
        result.Items.Should().BeEquivalentTo(["hello", "world"]);
    }

    [Fact]
    public void From_WithEmptyList_ReturnsEmptyItems()
    {
        var result = PagedResponseType<string>.From([], 0);
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }
}
