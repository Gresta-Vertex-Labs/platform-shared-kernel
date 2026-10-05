using FluentAssertions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-05: <see cref="SearchResults{TDocument}.ToPagedList"/> guard tests — <see cref="TotalHitsAccuracy.Estimated"/>
/// and <see cref="TotalHitsAccuracy.LowerBound"/> each return <c>TotalHitsNotExact</c>; a negative
/// <c>TotalHits</c>, an invalid page/page-size, or more hits than the page size returns
/// <c>InvalidSearchRequest</c>; and a valid <see cref="TotalHitsAccuracy.Exact"/> result, including one
/// whose <c>TotalHits</c> exceeds <see cref="int.MaxValue"/>, projects correctly while dropping facets,
/// rank, and highlights.
/// </summary>
public sealed class SearchResultsToPagedListTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    [Fact]
    public void Estimated_ReturnsTotalHitsNotExact_NamingRequireExactTotalHitsAsRemedy()
    {
        var results = new SearchResults<TestDocument>
        {
            Hits = [],
            TotalHits = 10,
            Accuracy = TotalHitsAccuracy.Estimated,
            Page = 1,
            PageSize = 20,
        };

        var result = results.ToPagedList();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.total_hits_not_exact");
        result.Error.Message.Should().Contain("RequireExactTotalHits");
    }

    [Fact]
    public void LowerBound_ReturnsTotalHitsNotExact()
    {
        var results = new SearchResults<TestDocument>
        {
            Hits = [],
            TotalHits = 10_000,
            Accuracy = TotalHitsAccuracy.LowerBound,
            Page = 1,
            PageSize = 20,
        };

        var result = results.ToPagedList();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.total_hits_not_exact");
    }

    [Fact]
    public void TotalHitsExceedingIntMaxValue_ProjectsWithoutLoss()
    {
        const long totalHits = (long)int.MaxValue + 1;
        var results = new SearchResults<TestDocument>
        {
            Hits = [],
            TotalHits = totalHits,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 1,
            PageSize = 20,
        };

        var result = results.ToPagedList();

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(totalHits);
    }

    [Fact]
    public void NegativeTotalHits_ReturnsInvalidSearchRequest()
    {
        var results = new SearchResults<TestDocument>
        {
            Hits = [],
            TotalHits = -1,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 1,
            PageSize = 20,
        };

        var result = results.ToPagedList();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void MoreHitsThanPageSize_ReturnsInvalidSearchRequest_InsteadOfThrowing()
    {
        var results = new SearchResults<TestDocument>
        {
            Hits =
            [
                new SearchHit<TestDocument> { Document = new TestDocument { DocumentId = "a" }, Rank = 0 },
                new SearchHit<TestDocument> { Document = new TestDocument { DocumentId = "b" }, Rank = 1 },
            ],
            TotalHits = 2,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 1,
            PageSize = 1,
        };

        var result = results.ToPagedList();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void PageLessThanOne_ReturnsInvalidSearchRequest()
    {
        var results = new SearchResults<TestDocument>
        {
            Hits = [],
            TotalHits = 0,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 0,
            PageSize = 20,
        };

        var result = results.ToPagedList();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void PageSizeLessThanOne_ReturnsInvalidSearchRequest()
    {
        var results = new SearchResults<TestDocument>
        {
            Hits = [],
            TotalHits = 0,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 1,
            PageSize = 0,
        };

        var result = results.ToPagedList();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void ValidExactResult_ProjectsToPagedList_WithMatchingPageMetadataAndItems()
    {
        var doc1 = new TestDocument { DocumentId = "doc-1" };
        var doc2 = new TestDocument { DocumentId = "doc-2" };

        var results = new SearchResults<TestDocument>
        {
            Hits =
            [
                new SearchHit<TestDocument>
                {
                    Document = doc1,
                    Rank = 0,
                    Highlights = new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["<em>match</em>"] },
                },
                new SearchHit<TestDocument> { Document = doc2, Rank = 1 },
            ],
            TotalHits = 42,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 2,
            PageSize = 20,
            Facets = new Dictionary<string, FacetResult> { ["status"] = new() { Field = "status" } },
        };

        var result = results.ToPagedList();

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(20);
        result.Value.TotalCount.Should().Be(42);
        result.Value.Items.Should().Equal(doc1, doc2);
    }

    [Fact]
    public void ValidExactResult_DropsFacetsRankAndHighlights_AsDocumented()
    {
        // PagedList<T> has no members for facets/rank/highlights at all — the assertion here is
        // structural: only Items/Page/PageSize/TotalCount survive the projection.
        var results = new SearchResults<TestDocument>
        {
            Hits = [new SearchHit<TestDocument> { Document = new TestDocument { DocumentId = "d" }, Rank = 0 }],
            TotalHits = 1,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = 1,
            PageSize = 20,
            Facets = new Dictionary<string, FacetResult> { ["f"] = new() { Field = "f" } },
        };

        var pagedList = results.ToPagedList().Value;

        var pagedListMemberNames = typeof(SharedKernel.Contracts.Pagination.PagedList<TestDocument>)
            .GetProperties()
            .Select(p => p.Name);

        pagedListMemberNames.Should().NotContain(["Facets", "Rank", "Highlights"]);
        pagedList.Items.Should().HaveCount(1);
    }
}
