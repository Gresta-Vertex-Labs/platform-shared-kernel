using System.Text.Json;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Contracts.Tests;

public sealed class PagedListTests
{
    private static readonly JsonSerializerOptions Pascal = new();

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(5_000_000_000, 1000, 5_000_000)]
    public void TotalPages_RoundsUp(long totalCount, int pageSize, long expected) =>
        PagedList<int>.Create([], 1, pageSize, totalCount).TotalPages.Should().Be(expected);

    [Fact]
    public void Navigation()
    {
        var middle = PagedList<int>.Create([1, 2], 2, 2, 5);
        middle.HasPreviousPage.Should().BeTrue();
        middle.HasNextPage.Should().BeTrue();

        var last = PagedList<int>.Create([5], 3, 2, 5);
        last.HasNextPage.Should().BeFalse();

        var pastTheEnd = PagedList<int>.Create([], 9, 2, 5);
        pastTheEnd.HasNextPage.Should().BeFalse();
        pastTheEnd.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Create_CopiesTheItems()
    {
        var source = new List<int> { 1, 2 };
        var page = PagedList<int>.Create(source, 1, 10, 2);

        source.Add(3);

        page.Items.Should().Equal(1, 2);
        page.Items.Should().NotBeAssignableTo<List<int>>();
    }

    [Fact]
    public void Create_AllowsATotalBelowTheItemCount_BecauseCountAndPageAreSeparateQueries() =>
        PagedList<int>.Create([1, 2, 3], 1, 10, 2).Items.Should().HaveCount(3);

    [Fact]
    public void Create_RejectsInvalidArguments()
    {
        FluentActions.Invoking(() => PagedList<int>.Create(null!, 1, 10, 0)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PagedList<int>.Create([], 0, 10, 0))
            .Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("page");
        FluentActions.Invoking(() => PagedList<int>.Create([], 1, 0, 0))
            .Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("pageSize");
        FluentActions.Invoking(() => PagedList<int>.Create([], 1, 10, -1))
            .Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("totalCount");
        FluentActions.Invoking(() => PagedList<int>.Create([1, 2, 3], 1, 2, 3))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("items");
    }

    [Fact]
    public void CreateFromRequest_AndEmpty()
    {
        var request = PageRequest.Create(3, 25).Value;

        var page = PagedList<string>.Create(["a"], request, 51);
        page.Page.Should().Be(3);
        page.PageSize.Should().Be(25);

        var empty = PagedList<string>.Empty(request);
        empty.Items.Should().BeEmpty();
        empty.TotalCount.Should().Be(0);
        empty.Page.Should().Be(3);
    }

    [Fact]
    public void Map_ProjectsItemsAndKeepsPosition()
    {
        var mapped = PagedList<int>.Create([1, 2], 2, 2, 7).Map(i => $"#{i}");

        mapped.Should().Be(PagedList<string>.Create(["#1", "#2"], 2, 2, 7));
    }

    [Fact]
    public void Equality_ComparesItemsByValue()
    {
        var a = PagedList<int>.Create(new List<int> { 1, 2 }, 1, 2, 2);
        var b = PagedList<int>.Create(new[] { 1, 2 }, 1, 2, 2);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(PagedList<int>.Create([2, 1], 1, 2, 2));
        a.Should().NotBe(PagedList<int>.Create([1, 2], 1, 2, 3));
    }

    [Fact]
    public void Json_HasFixedNamesAndRoundTrips()
    {
        var page = PagedList<int>.Create([1, 2], 2, 2, 5);

        var json = JsonSerializer.Serialize(page, Pascal);

        json.Should().Be("""{"items":[1,2],"page":2,"pageSize":2,"totalCount":5,"totalPages":3,"hasNextPage":true,"hasPreviousPage":true}""");
        JsonSerializer.Deserialize<PagedList<int>>(json, Pascal).Should().Be(page);
    }

    [Theory]
    [InlineData("""{"page":1,"pageSize":10,"totalCount":0}""")]
    [InlineData("""{"items":[],"page":0,"pageSize":10,"totalCount":0}""")]
    [InlineData("""{"items":[],"page":1,"pageSize":0,"totalCount":0}""")]
    [InlineData("""{"items":[],"page":1,"pageSize":10,"totalCount":-1}""")]
    [InlineData("""{"items":[1,2,3],"page":1,"pageSize":2,"totalCount":3}""")]
    public void Json_RejectsInvalidDocuments(string json) =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<PagedList<int>>(json, Pascal)).Should().Throw<JsonException>();
}
