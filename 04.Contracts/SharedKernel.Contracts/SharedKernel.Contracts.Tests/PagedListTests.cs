using System.Text.Json;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Contracts.Serialization;

namespace SharedKernel.Contracts.Tests;

public sealed class PagedListTests
{
    // ─── TotalPages computation ───────────────────────────────────────────────

    [Fact]
    public void TotalPages_ExactDivision_ReturnsCorrectCount()
    {
        var list = PagedList<string>.Create([], 1, 10, 100);
        list.TotalPages.Should().Be(10);
    }

    [Fact]
    public void TotalPages_WithRemainder_RoundsUp()
    {
        var list = PagedList<string>.Create([], 1, 10, 101);
        list.TotalPages.Should().Be(11);
    }

    [Fact]
    public void TotalPages_TotalCountSmallerThanPageSize_ReturnsOne()
    {
        var list = PagedList<string>.Create([], 1, 10, 5);
        list.TotalPages.Should().Be(1);
    }

    [Fact]
    public void TotalPages_TotalCountIsZero_ReturnsZero()
    {
        var list = PagedList<string>.Create([], 1, 10, 0);
        list.TotalPages.Should().Be(0);
    }

    // ─── HasNextPage / HasPreviousPage ────────────────────────────────────────

    [Fact]
    public void FirstPage_HasNoPreviousPage()
    {
        var list = PagedList<string>.Create([], 1, 10, 100);
        list.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void FirstPage_HasNextPageWhenMorePagesExist()
    {
        var list = PagedList<string>.Create([], 1, 10, 100);
        list.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void LastPage_HasNoNextPage()
    {
        var list = PagedList<string>.Create([], 10, 10, 100);
        list.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void LastPage_HasPreviousPage()
    {
        var list = PagedList<string>.Create([], 10, 10, 100);
        list.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void MiddlePage_HasBothNextAndPreviousPage()
    {
        var list = PagedList<string>.Create([], 5, 10, 100);
        list.HasNextPage.Should().BeTrue();
        list.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void SinglePage_HasNeitherNextNorPreviousPage()
    {
        // TotalCount fits on one page — TotalPages = 1
        var list = PagedList<string>.Create([], 1, 10, 5);
        list.HasNextPage.Should().BeFalse();
        list.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void EmptyList_HasNeitherNextNorPreviousPage()
    {
        // TotalCount = 0 → TotalPages = 0 → HasNextPage = false (1 < 0 is false)
        var list = PagedList<string>.Create([], 1, 10, 0);
        list.HasNextPage.Should().BeFalse();
        list.HasPreviousPage.Should().BeFalse();
    }

    // ─── Create factory guards ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_PageLessThanOne_ThrowsArgumentOutOfRangeException(int invalidPage)
    {
        var act = () => PagedList<string>.Create([], invalidPage, 10, 100);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_PageSizeLessThanOne_ThrowsArgumentOutOfRangeException(int invalidPageSize)
    {
        var act = () => PagedList<string>.Create([], 1, invalidPageSize, 100);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("pageSize");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_TotalCountNegative_ThrowsArgumentOutOfRangeException(int invalidTotalCount)
    {
        var act = () => PagedList<string>.Create([], 1, 10, invalidTotalCount);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("totalCount");
    }

    [Fact]
    public void Create_NullItems_ThrowsArgumentNullException()
    {
        var act = () => PagedList<string>.Create(null!, 1, 10, 0);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("items");
    }

    [Fact]
    public void Create_BoundaryValues_DoesNotThrow()
    {
        // Boundary values: page = 1, pageSize = 1, totalCount = 0
        var act = () => PagedList<string>.Create([], 1, 1, 0);
        act.Should().NotThrow();
    }

    // ─── Page is 1-based ──────────────────────────────────────────────────────

    [Fact]
    public void Create_PageOneIsFirstPage()
    {
        var list = PagedList<string>.Create(["a", "b"], 1, 10, 2);
        list.Page.Should().Be(1);
        list.HasPreviousPage.Should().BeFalse();
    }

    // ─── Structural equality ──────────────────────────────────────────────────

    [Fact]
    public void TwoInstancesWithSameFields_AreEqual()
    {
        IReadOnlyList<string> items = ["x", "y"];
        var a = PagedList<string>.Create(items, 2, 10, 50);
        var b = PagedList<string>.Create(items, 2, 10, 50);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void TwoInstancesWithDifferentPage_AreNotEqual()
    {
        IReadOnlyList<string> items = ["x"];
        var a = PagedList<string>.Create(items, 1, 10, 50);
        var b = PagedList<string>.Create(items, 2, 10, 50);

        a.Should().NotBe(b);
    }

    // ─── STJ round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void SerjDeserj_PagedListOfString_RoundTripsCorrectly()
    {
        var original = PagedList<string>.Create(["hello", "world"], 2, 5, 42);

        // Demonstrates the consumer-service pattern: use a service-level context that registers
        // the concrete type argument, merged with ContractsJsonContext via TypeInfoResolverChain.
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var json = JsonSerializer.Serialize(original, options);
        json.Should().Contain("\"page\":2");
        json.Should().Contain("\"pageSize\":5");
        json.Should().Contain("\"totalCount\":42");
        json.Should().Contain("\"items\":");
        json.Should().Contain("hello");
        json.Should().Contain("world");

        // Deserialize and verify round-trip fidelity
        var deserialized = JsonSerializer.Deserialize<PagedList<string>>(json, options);
        deserialized.Should().NotBeNull();
        deserialized!.Page.Should().Be(original.Page);
        deserialized.PageSize.Should().Be(original.PageSize);
        deserialized.TotalCount.Should().Be(original.TotalCount);
        deserialized.Items.Should().BeEquivalentTo(original.Items);
    }
}
