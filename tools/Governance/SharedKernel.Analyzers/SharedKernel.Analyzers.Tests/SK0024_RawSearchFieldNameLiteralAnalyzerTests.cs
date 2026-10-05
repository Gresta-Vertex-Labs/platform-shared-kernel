using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0024 <see cref="RawSearchFieldNameLiteralAnalyzer"/> — WO-044 P-278.</summary>
/// <remarks>
/// <para>T-200: Fire path — <c>SearchFilter.Eq("field", value)</c> raw literal triggers SK0024.</para>
/// <para>
/// T-201: Fire path — <c>IQueryBuilder&lt;TDocument&gt;.OrderBy("field")</c> raw literal triggers
/// SK0024.
/// </para>
/// <para>T-202: Fire path — <c>.SearchingIn("a", "b")</c> triggers SK0024 on each literal element.</para>
/// <para>T-203: Pass path — <c>SearchFilter.Eq(nameof(ProductDocument.Title), value)</c> does not
/// trigger SK0024.</para>
/// <para>
/// T-204: Pass path — <c>.OrderBy(ProductDocumentFields.Title)</c> (domain-local field-constants
/// class reference) does not trigger SK0024.
/// </para>
/// <para>
/// Fixture stubs mirror the REAL shipped <c>SharedKernel.Search.Abstractions</c> source exactly
/// (namespaces <c>SharedKernel.Search.Abstractions.Querying</c> and
/// <c>SharedKernel.Search.Abstractions.Models</c>, confirmed 2026-07-24 against
/// <c>src/Infrastructure/Search/SharedKernel.Search.Abstractions/Querying/IQueryBuilder.cs</c> and
/// <c>Models/SearchFilter.cs</c>) — an in-compilation stub, not a <c>ProjectReference</c>, per the
/// SK0017/SK0022 precedent of self-contained analyzer test fixtures.
/// </para>
/// </remarks>
public class SK0024_RawSearchFieldNameLiteralAnalyzerTests
{
    private const string AbstractionsStubs = """
        namespace SharedKernel.Search.Abstractions.Abstractions
        {
            public interface ISearchDocument
            {
                string DocumentId { get; }
            }
        }

        namespace SharedKernel.Search.Abstractions.Models
        {
            public readonly struct SearchValue
            {
                public static implicit operator SearchValue(string value) => default;
                public static implicit operator SearchValue(long value) => default;
            }

            public abstract class SearchFilter
            {
                public static SearchFilter Eq(string field, SearchValue value) => null!;
                public static SearchFilter Ne(string field, SearchValue value) => null!;
                public static SearchFilter In(string field, params SearchValue[] values) => null!;
                public static SearchFilter Between(
                    string field,
                    SearchValue? from,
                    SearchValue? to,
                    bool fromInclusive = true,
                    bool toInclusive = true) => null!;
                public static SearchFilter Exists(string field) => null!;
            }
        }

        namespace SharedKernel.Search.Abstractions.Querying
        {
            using SharedKernel.Search.Abstractions.Abstractions;
            using SharedKernel.Search.Abstractions.Models;

            public interface IQueryBuilder<TDocument>
                where TDocument : class, ISearchDocument
            {
                IQueryBuilder<TDocument> Matching(string? freeText);
                IQueryBuilder<TDocument> SearchingIn(params string[] fields);
                IQueryBuilder<TDocument> Where(SearchFilter filter);
                IQueryBuilder<TDocument> OrderBy(string field);
                IQueryBuilder<TDocument> OrderByDescending(string field);
                IQueryBuilder<TDocument> Faceting(params string[] facetFields);
                IQueryBuilder<TDocument> WithNumericFacetStats(params string[] facetFields);
                IQueryBuilder<TDocument> Returning(params string[] fields);
            }
        }

        """;

    private const string ProductDocumentStub = """
        namespace Fixture.Search
        {
            using SharedKernel.Search.Abstractions.Abstractions;

            public sealed class ProductDocument : ISearchDocument
            {
                public string DocumentId => "1";
                public string Title => "widget";
            }

            public static class ProductDocumentFields
            {
                public const string Title = "Title";
            }
        }

        """;

    private static CSharpAnalyzerTest<RawSearchFieldNameLiteralAnalyzer, DefaultVerifier> CreateTest(string source) =>
        new()
        {
            TestCode = AbstractionsStubs + ProductDocumentStub + source,
        };

    // ---------------------------------------------------------------------------
    // T-200 — Fire path: SearchFilter.Eq("field", value)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_SearchFilterEqRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Models;

                public class ProductFilters
                {
                    public SearchFilter TitleEquals(string value) =>
                        SearchFilter.Eq({|SK0024:"Title"|}, value);
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-201 — Fire path: IQueryBuilder<TDocument>.OrderBy("field")
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_QueryBuilderOrderByRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> SortByTitle(IQueryBuilder<ProductDocument> builder) =>
                        builder.OrderBy({|SK0024:"Title"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_QueryBuilderOrderByDescendingRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> SortByTitleDesc(IQueryBuilder<ProductDocument> builder) =>
                        builder.OrderByDescending({|SK0024:"Title"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-202 — Fire path: .SearchingIn("a", "b") on each literal element
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_SearchingInMultipleRawLiterals_ReportsSk0024OnEachElement()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> RestrictFields(IQueryBuilder<ProductDocument> builder) =>
                        builder.SearchingIn({|SK0024:"Title"|}, {|SK0024:"Description"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_FacetingArrayInitializerRawLiterals_ReportsSk0024OnEachElement()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> FacetFields(IQueryBuilder<ProductDocument> builder) =>
                        builder.Faceting(new[] { {|SK0024:"Status"|}, {|SK0024:"Category"|} });
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ReturningRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> RestrictReturnedFields(IQueryBuilder<ProductDocument> builder) =>
                        builder.Returning({|SK0024:"Title"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_WithNumericFacetStatsRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> PriceStats(IQueryBuilder<ProductDocument> builder) =>
                        builder.WithNumericFacetStats({|SK0024:"Price"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Fire path — SearchFilter's other four static factories
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_SearchFilterNeRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Models;

                public class ProductFilters
                {
                    public SearchFilter TitleNotEquals(string value) =>
                        SearchFilter.Ne({|SK0024:"Title"|}, value);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_SearchFilterInRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Models;

                public class ProductFilters
                {
                    public SearchFilter StatusIn(string a, string b) =>
                        SearchFilter.In({|SK0024:"Status"|}, a, b);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_SearchFilterBetweenRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Models;

                public class ProductFilters
                {
                    public SearchFilter PriceBetween(long from, long to) =>
                        SearchFilter.Between({|SK0024:"Price"|}, from, to);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_SearchFilterExistsRawLiteral_ReportsSk0024()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Models;

                public class ProductFilters
                {
                    public SearchFilter HasDiscount() =>
                        SearchFilter.Exists({|SK0024:"DiscountPercent"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-203 — Pass path: SearchFilter.Eq(nameof(ProductDocument.Title), value)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_SearchFilterEqWithNameof_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Models;

                public class ProductFilters
                {
                    public SearchFilter TitleEquals(string value) =>
                        SearchFilter.Eq(nameof(ProductDocument.Title), value);
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-204 — Pass path: .OrderBy(ProductDocumentFields.Title)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_QueryBuilderOrderByWithFieldConstantsClass_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> SortByTitle(IQueryBuilder<ProductDocument> builder) =>
                        builder.OrderBy(ProductDocumentFields.Title);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_SearchingInWithFieldConstantsClass_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                using SharedKernel.Search.Abstractions.Querying;

                public class ProductQueries
                {
                    public IQueryBuilder<ProductDocument> RestrictFields(IQueryBuilder<ProductDocument> builder) =>
                        builder.SearchingIn(ProductDocumentFields.Title, nameof(ProductDocument.Title));
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path proving the LINQ collision this rule exists to avoid: an unrelated
    /// <c>OrderBy(string)</c>-shaped extension method on a type outside
    /// <c>SharedKernel.Search.Abstractions.Querying</c> must never fire SK0024.
    /// </summary>
    [Fact]
    public async Task PassPath_UnrelatedOrderByMethodOnDifferentType_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Unrelated
            {
                public class ReportBuilder
                {
                    public ReportBuilder OrderBy(string field) => this;
                }

                public class ReportQueries
                {
                    public ReportBuilder Sort(ReportBuilder builder) => builder.OrderBy("Title");
                }
            }
            """
        );
        await test.RunAsync();
    }
}
