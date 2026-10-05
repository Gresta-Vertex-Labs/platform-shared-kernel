using System.Text.Json;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Contracts.Tests;

public sealed class CursorPagedListTests
{
    private static readonly JsonSerializerOptions Pascal = new();

    [Fact]
    public void HasMore_FollowsTheCursor()
    {
        CursorPagedList<int>.Create([1], "v1.abc").HasMore.Should().BeTrue();
        CursorPagedList<int>.Create([1], null).HasMore.Should().BeFalse();
        CursorPagedList<int>.Empty().Should().Be(CursorPagedList<int>.Create([], null));
    }

    [Fact]
    public void Create_RejectsInvalidArguments()
    {
        FluentActions.Invoking(() => CursorPagedList<int>.Create(null!, null)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => CursorPagedList<int>.Create([], " ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CursorPagedList<int>.Create([], new string('c', PageCursor.MaxLength + 1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_CopiesTheItems()
    {
        var source = new List<int> { 1 };
        var page = CursorPagedList<int>.Create(source, null);

        source.Add(2);

        page.Items.Should().Equal(1);
    }

    [Fact]
    public void FromLookahead_ExtraItemMeansAnotherPage()
    {
        string? cursorBuiltFrom = null;

        var page = CursorPagedList<int>.FromLookahead([10, 20, 30], 2, last =>
        {
            cursorBuiltFrom = last.ToString();
            return PageCursor.Encode(last, last);
        });

        page.Items.Should().Equal(10, 20);
        page.HasMore.Should().BeTrue();
        cursorBuiltFrom.Should().Be("20");
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 10 })]
    [InlineData(new[] { 10, 20 })]
    public void FromLookahead_NoExtraItemMeansLastPage(int[] fetched)
    {
        var page = CursorPagedList<int>.FromLookahead(fetched, 2, _ => throw new InvalidOperationException("not called"));

        page.Items.Should().Equal(fetched);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public void FromLookahead_RejectsInvalidArguments()
    {
        FluentActions.Invoking(() => CursorPagedList<int>.FromLookahead([1, 2, 3, 4], 2, _ => "c"))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("fetched");
        FluentActions.Invoking(() => CursorPagedList<int>.FromLookahead([1], 0, _ => "c"))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CursorPagedList<int>.FromLookahead([1, 2], 1, _ => ""))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("cursorFor");
        FluentActions.Invoking(() => CursorPagedList<int>.FromLookahead([1], 1, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Map_KeepsTheCursor() =>
        CursorPagedList<int>.Create([1, 2], "v1.x").Map(i => i * 10)
            .Should().Be(CursorPagedList<int>.Create([10, 20], "v1.x"));

    [Fact]
    public void Equality_ComparesItemsByValue()
    {
        var a = CursorPagedList<string>.Create(new List<string> { "a" }, "v1.x");
        var b = CursorPagedList<string>.Create(new[] { "a" }, "v1.x");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(CursorPagedList<string>.Create(["a"], null));
    }

    [Fact]
    public void Json_HasFixedNamesAndRoundTrips()
    {
        var page = CursorPagedList<int>.Create([1], "v1.x");

        var json = JsonSerializer.Serialize(page, Pascal);

        json.Should().Be("""{"items":[1],"nextCursor":"v1.x","hasMore":true}""");
        JsonSerializer.Deserialize<CursorPagedList<int>>(json, Pascal).Should().Be(page);
    }

    [Theory]
    [InlineData("""{"nextCursor":null}""")]
    [InlineData("""{"items":[],"nextCursor":"  "}""")]
    public void Json_RejectsInvalidDocuments(string json) =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<CursorPagedList<int>>(json, Pascal)).Should().Throw<JsonException>();

    [Fact]
    public void Json_IgnoresAnIncomingHasMore() =>
        JsonSerializer.Deserialize<CursorPagedList<int>>("""{"items":[],"nextCursor":null,"hasMore":true}""", Pascal)!
            .HasMore.Should().BeFalse();
}
