using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Querying;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/SearchRequest<T> types;
// alias ours explicitly, mirroring the production translator's own convention.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Querying;

/// <summary>
/// T-18: <see cref="ElasticSearchFilterCompiler"/> unit tests (container-free) — one case per
/// <see cref="SearchFilter"/> node, asserting every clause lands in <see cref="BoolQuery.Filter"/>
/// (filter context, non-scoring); empty clause collections are omitted entirely rather than assigned
/// as empty objects; <c>In</c> with a single value; <see cref="TenantScope"/> as the outermost clause;
/// and a multi-field <c>Sort</c> regression test guarding against the client's known chained-descriptor
/// defect (issue #8471).
/// </summary>
public sealed class ElasticSearchFilterCompilerTests
{
    // ---------------------------------------------------------------------------
    // One case per node
    // ---------------------------------------------------------------------------

    [Fact]
    public void Compile_EqualFilter_ProducesTermQuery()
    {
        var filter = SearchFilter.Eq("status", SearchValue.From("active"));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Term.Should().NotBeNull();
        query.Term!.Field.Name.Should().Be("status");
        query.Term.Value.Should().Be(FieldValue.String("active"));
    }

    [Fact]
    public void Compile_NotEqualFilter_ProducesBoolQuery_WithTermInMustNot()
    {
        var filter = SearchFilter.Ne("status", SearchValue.From("archived"));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Bool.Should().NotBeNull();
        query.Bool!.MustNot.Should().ContainSingle();
        var mustNotClause = query.Bool.MustNot!.Single();
        mustNotClause.Term!.Field.Name.Should().Be("status");
        mustNotClause.Term.Value.Should().Be(FieldValue.String("archived"));
    }

    [Fact]
    public void Compile_InFilter_ProducesTermsQuery_WithFieldAndValues()
    {
        var filter = SearchFilter.In("category", SearchValue.From("a"), SearchValue.From("b"));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Terms.Should().NotBeNull();
        query.Terms!.Field.Name.Should().Be("category");
        query.Terms.Terms.Value1.Should().Contain(FieldValue.String("a")).And.Contain(FieldValue.String("b"));
    }

    [Fact]
    public void Compile_InFilter_WithSingleValue_ProducesTermsQuery_WithOneValue()
    {
        var filter = SearchFilter.In("category", SearchValue.From("only"));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Terms!.Terms.Value1.Should().ContainSingle().Which.Should().Be(FieldValue.String("only"));
    }

    [Fact]
    public void Compile_RangeFilter_WithNumericBounds_ProducesNumberRangeQuery()
    {
        var filter = SearchFilter.Between("price", SearchValue.From(10L), SearchValue.From(100L));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        var range = query.Range.Should().BeOfType<NumberRangeQuery>().Subject;
        range.Field.Name.Should().Be("price");
        // Elastic.Clients.Elasticsearch.Number has no public properties for FluentAssertions'
        // structural formatter to compare — Be(10) mis-formats as "Number{ }" even when the
        // underlying values match, so bounds must be cast to the explicit Number type for equality.
        range.Gte.Should().Be((Number)10.0);
        range.Lte.Should().Be((Number)100.0);
        range.Gt.Should().BeNull();
        range.Lt.Should().BeNull();
    }

    [Fact]
    public void Compile_RangeFilter_WithExclusiveNumericBounds_UsesGtAndLt()
    {
        var filter = SearchFilter.Between(
            "price", SearchValue.From(10L), SearchValue.From(100L), fromInclusive: false, toInclusive: false);

        var query = ElasticSearchFilterCompiler.Compile(filter);

        var range = query.Range.Should().BeOfType<NumberRangeQuery>().Subject;
        range.Gt.Should().Be((Number)10.0);
        range.Lt.Should().Be((Number)100.0);
        range.Gte.Should().BeNull();
        range.Lte.Should().BeNull();
    }

    [Fact]
    public void Compile_RangeFilter_WithDateTimeOffsetBounds_ProducesDateRangeQuery()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var filter = SearchFilter.Between("createdAt", SearchValue.From(from), SearchValue.From(to));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        var range = query.Range.Should().BeOfType<DateRangeQuery>().Subject;
        range.Field.Name.Should().Be("createdAt");
        range.Gte!.ToString().Should().Contain("2026-01-01");
        range.Lte!.ToString().Should().Contain("2026-12-31");
    }

    [Fact]
    public void Compile_ExistsFilter_ProducesExistsQuery()
    {
        var filter = SearchFilter.Exists("optionalField");

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Exists.Should().NotBeNull();
        query.Exists!.Field.Name.Should().Be("optionalField");
    }

    [Fact]
    public void Compile_AndFilter_ProducesBoolQuery_WithOperandsInFilter()
    {
        var filter = SearchFilter.All(
            SearchFilter.Eq("a", SearchValue.From(1L)),
            SearchFilter.Eq("b", SearchValue.From(2L)));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Bool.Should().NotBeNull();
        query.Bool!.Filter.Should().HaveCount(2);
        query.Bool.Filter!.Select(q => q.Term!.Field.Name).Should().Equal("a", "b");
    }

    [Fact]
    public void Compile_AndFilter_WithNoOperands_ProducesClauselessBoolQuery_NotAnEmptyObjectAssignment()
    {
        var filter = SearchFilter.All();

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Bool.Should().NotBeNull();
        query.Bool!.Filter.Should().BeNull(
            "the client has no conditionless-query support — an empty clause collection must be " +
            "omitted (left null) rather than assigned as an empty collection");
    }

    [Fact]
    public void Compile_OrFilter_ProducesBoolQuery_WithOperandsInShould()
    {
        var filter = SearchFilter.Any(
            SearchFilter.Eq("a", SearchValue.From(1L)),
            SearchFilter.Eq("b", SearchValue.From(2L)));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Bool.Should().NotBeNull();
        query.Bool!.Should.Should().HaveCount(2);
        query.Bool.Filter.Should().BeNull();
    }

    [Fact]
    public void Compile_OrFilter_WithNoOperands_ProducesClauselessBoolQuery()
    {
        var filter = SearchFilter.Any();

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Bool.Should().NotBeNull();
        query.Bool!.Should.Should().BeNull();
    }

    [Fact]
    public void Compile_NotFilter_ProducesBoolQuery_WithOperandInMustNot()
    {
        var filter = SearchFilter.Negate(SearchFilter.Eq("status", SearchValue.From("archived")));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Bool.Should().NotBeNull();
        query.Bool!.MustNot.Should().ContainSingle();
        query.Bool.MustNot!.Single().Term!.Field.Name.Should().Be("status");
    }

    // ---------------------------------------------------------------------------
    // Value formatting
    // ---------------------------------------------------------------------------

    [Fact]
    public void Compile_Int64Value_MapsToLongFieldValue()
    {
        var filter = SearchFilter.Eq("count", SearchValue.From(42L));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Term!.Value.Should().Be(FieldValue.Long(42L));
    }

    [Fact]
    public void Compile_DoubleValue_MapsToDoubleFieldValue()
    {
        var filter = SearchFilter.Eq("rating", SearchValue.From(4.5));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Term!.Value.Should().Be(FieldValue.Double(4.5));
    }

    [Fact]
    public void Compile_BooleanValue_MapsToBooleanFieldValue()
    {
        var filter = SearchFilter.Eq("isActive", SearchValue.From(true));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Term!.Value.Should().Be(FieldValue.Boolean(true));
    }

    [Fact]
    public void Compile_DateTimeOffsetValue_MapsToStrictIso8601StringFieldValue()
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var filter = SearchFilter.Eq("createdAt", SearchValue.From(timestamp));

        var query = ElasticSearchFilterCompiler.Compile(filter);

        query.Term!.Value.Value.Should().Be(timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------------------
    // TenantScope injection
    // ---------------------------------------------------------------------------

    [Fact]
    public void CompileWithTenantScope_WithNoCallerFilter_EmitsOnlyTenantTermClause()
    {
        var definition = TenantedDefinition();

        var result = ElasticSearchFilterCompiler.CompileWithTenantScope(definition, null, TenantScope.For(TestTenants.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Term!.Field.Name.Should().Be("tenantId");
        result.Value.Term.Value.Should().Be(FieldValue.String(TestTenants.TenantA.ToString()));
    }

    [Fact]
    public void CompileWithTenantScope_WithCallerFilter_PrependsTenantClauseAsOutermostFilterEntry()
    {
        var definition = TenantedDefinition();
        var callerFilter = SearchFilter.Eq("status", SearchValue.From("active"));

        var result = ElasticSearchFilterCompiler.CompileWithTenantScope(definition, callerFilter, TenantScope.For(TestTenants.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Bool.Should().NotBeNull();
        result.Value.Bool!.Filter.Should().HaveCount(2);
        result.Value.Bool.Filter!.First().Term!.Field.Name.Should().Be("tenantId");
        result.Value.Bool.Filter!.Last().Term!.Field.Name.Should().Be("status");
    }

    [Fact]
    public void CompileWithTenantScope_OnUndeclaredTenantField_IgnoresTenantScope()
    {
        var definition = SearchIndexDefinition.Create("products", []).Value;

        var result = ElasticSearchFilterCompiler.CompileWithTenantScope(definition, null, TenantScope.For(TestTenants.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void CompileWithTenantScope_OnTenantedIndex_WithTenantScopeGlobal_FailsClosed()
    {
        var definition = TenantedDefinition();

        var result = ElasticSearchFilterCompiler.CompileWithTenantScope(definition, null, TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.tenant_scope_missing");
    }

    // ---------------------------------------------------------------------------
    // Multi-field sort regression (issue #8471) — belongs to the request translator, not the filter
    // compiler, but is exercised here alongside the compiler suite per T-18's own scope.
    // ---------------------------------------------------------------------------

    [Fact]
    public void MultiFieldSort_AssignedAsFullCollectionOnce_PreservesEveryField_NotOnlyTheLast()
    {
        var definition = SearchIndexDefinition.Create("products", []).Value;
        var request = SearchRequest.Default with
        {
            Sort =
            [
                SearchSort.Ascending("name"),
                SearchSort.Descending("createdAt"),
                SearchSort.Ascending("price"),
            ],
        };

        var translated = ElasticSearchRequestTranslator.Translate<object>("products", definition, request, null);

        translated.Sort.Should().NotBeNull();
        translated.Sort!.Should().HaveCount(3,
            "issue #8471 in the ElasticSearch .NET client silently keeps only the last field when sort " +
            "options are assigned via chained descriptor calls — this asserts the full collection survives");
        translated.Sort!.Select(s => s.Field!.Field.Name).Should().Equal("name", "createdAt", "price");
    }

    private static SearchIndexDefinition TenantedDefinition() => new SearchIndexDefinitionBuilder("products")
        .TenantField("tenantId")
        .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
        .Field("status", SearchFieldKind.Keyword, filterable: true)
        .Build()
        .Value;
}
