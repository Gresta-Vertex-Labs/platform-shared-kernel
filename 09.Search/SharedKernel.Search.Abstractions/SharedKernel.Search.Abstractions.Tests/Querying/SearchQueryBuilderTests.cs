using FluentAssertions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Querying;

namespace SharedKernel.Search.Abstractions.Tests.Querying;

/// <summary>
/// T-04: <see cref="SearchQueryBuilder{TDocument}"/> immutability and semantics tests — every method
/// returns a new instance and leaves the source builder unmodified; repeated <c>Where(...)</c> calls
/// AND together rather than replacing (the single most important assertion in this suite);
/// <c>OrderBy</c>/<c>OrderByDescending</c> compose in call order; and <c>Build()</c> rejects every
/// provider-independent invariant violation.
/// </summary>
public sealed class SearchQueryBuilderTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    [Fact]
    public void Matching_ReturnsNewInstance_LeavingSourceUnmodified()
    {
        var original = SearchQuery.For<TestDocument>();

        var modified = original.Matching("hello");

        modified.Should().NotBeSameAs(original);
        // The source is unmodified — proven by building the untouched original and observing FreeText is null.
        original.Build().Value.FreeText.Should().BeNull();
        modified.Build().Value.FreeText.Should().Be("hello");
    }

    [Fact]
    public void EveryBuilderMethod_ReturnsNewInstance()
    {
        var original = SearchQuery.For<TestDocument>();

        IQueryBuilder<TestDocument>[] results =
        [
            original.Matching("x"),
            original.MatchAllTerms(true),
            original.SearchingIn("field"),
            original.Where(SearchFilter.Eq("a", SearchValue.From(1L))),
            original.OrderBy("a"),
            original.OrderByDescending("b"),
            original.Page(2, 10),
            original.RequireExactTotalHits(),
            original.Faceting("f"),
            original.WithNumericFacetStats("n"),
            original.Highlighting(new HighlightRequest { Fields = ["a"] }),
            original.Returning("a"),
        ];

        results.Should().OnlyContain(r => !ReferenceEquals(r, original));
    }

    [Fact]
    public void PartiallyBuiltQuery_CanBeSafelyReusedAsATemplate()
    {
        var template = SearchQuery.For<TestDocument>().Matching("shared");

        var first = template.Where(SearchFilter.Eq("tenant", SearchValue.From("a"))).Build().Value;
        var second = template.Where(SearchFilter.Eq("tenant", SearchValue.From("b"))).Build().Value;

        first.FreeText.Should().Be("shared");
        second.FreeText.Should().Be("shared");
        ((EqualFilter)first.Filter!).Value.AsString.Should().Be("a");
        ((EqualFilter)second.Filter!).Value.AsString.Should().Be("b");
    }

    [Fact]
    public void RepeatedWhereCalls_AndTogether_RatherThanReplacing()
    {
        var first = SearchFilter.Eq("status", SearchValue.From("active"));
        var second = SearchFilter.Eq("tenant", SearchValue.From("acme"));

        var request = SearchQuery.For<TestDocument>()
            .Where(first)
            .Where(second)
            .Build()
            .Value;

        var combined = request.Filter.Should().BeOfType<AndFilter>().Subject;
        combined.Operands.Should().Equal(first, second);
    }

    [Fact]
    public void ThreeRepeatedWhereCalls_AndAllThreeTogether()
    {
        var a = SearchFilter.Eq("a", SearchValue.From(1L));
        var b = SearchFilter.Eq("b", SearchValue.From(2L));
        var c = SearchFilter.Eq("c", SearchValue.From(3L));

        var request = SearchQuery.For<TestDocument>().Where(a).Where(b).Where(c).Build().Value;

        // Where() combines via SearchFilter.All(_filter, filter), which nests rather than flattens
        // (AndFilter(AndFilter(a, b), c)) — AND is associative, so the tree shape is not the
        // invariant under test. What matters is that all three predicates survive, AND'ed, with none
        // dropped or replaced by the later calls.
        var leaves = FlattenAndOperands(request.Filter!);
        leaves.Should().Equal(a, b, c);
    }

    private static List<SearchFilter> FlattenAndOperands(SearchFilter filter)
    {
        if (filter is not AndFilter and)
        {
            return [filter];
        }

        var flattened = new List<SearchFilter>();
        foreach (var operand in and.Operands)
        {
            flattened.AddRange(FlattenAndOperands(operand));
        }

        return flattened;
    }

    [Fact]
    public void OrderByThenOrderByDescending_ProducesTwoSortsInCallOrder()
    {
        var request = SearchQuery.For<TestDocument>()
            .OrderBy("name")
            .OrderByDescending("createdAt")
            .Build()
            .Value;

        request.Sort.Should().HaveCount(2);
        request.Sort[0].Field.Should().Be("name");
        request.Sort[0].Direction.Should().Be(SortDirection.Ascending);
        request.Sort[1].Field.Should().Be("createdAt");
        request.Sort[1].Direction.Should().Be(SortDirection.Descending);
    }

    [Fact]
    public void Build_WithPageLessThanOne_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>().Page(0, 20).Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithPageSizeLessThanOne_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>().Page(1, 0).Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithPageSizeAboveMaxPageSize_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>().Page(1, SearchWellKnown.MaxPageSize + 1).Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithEmptyFieldNameInSearchingIn_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>().SearchingIn("name", "").Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithEmptyFieldNameInFaceting_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>().Faceting("status", "  ").Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithDuplicateSortField_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>().OrderBy("name").OrderByDescending("name").Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithEmptyHighlightFieldList_ReturnsInvalidSearchRequest()
    {
        var result = SearchQuery.For<TestDocument>()
            .Highlighting(new HighlightRequest { Fields = [] })
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public void Build_WithValidRequest_Succeeds()
    {
        var result = SearchQuery.For<TestDocument>()
            .Matching("hello")
            .MatchAllTerms(true)
            .SearchingIn("name", "description")
            .Where(SearchFilter.Eq("status", SearchValue.From("active")))
            .OrderBy("name")
            .Page(2, 10)
            .RequireExactTotalHits()
            .Faceting("category")
            .WithNumericFacetStats("price")
            .Highlighting(new HighlightRequest { Fields = ["name"] })
            .Returning("id", "name")
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.FreeText.Should().Be("hello");
        result.Value.MatchAllTerms.Should().BeTrue();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(10);
        result.Value.RequireExactTotalHits.Should().BeTrue();
    }

    [Fact]
    public void Default_Page_IsOneBased()
    {
        var request = SearchQuery.For<TestDocument>().Build().Value;

        request.Page.Should().Be(1);
    }
}
