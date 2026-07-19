using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Querying;

namespace SharedKernel.Search.Meilisearch.Tests.Querying;

/// <summary>
/// T-10: <see cref="MeilisearchFilterCompiler"/> unit tests (container-free — the compiler emits a
/// string) — one case per <see cref="SearchFilter"/> node; string escaping/quoting; numerics/booleans
/// emitted bare; <see cref="DateTimeOffset"/> as Unix epoch seconds; unconditional parentheses around
/// every <c>Or</c>/<c>Not</c> operand; and the golden assertion that <see cref="TenantScope"/> appears
/// as the outermost <c>AND</c> regardless of the caller's filter shape.
/// </summary>
public sealed class MeilisearchFilterCompilerTests
{
    // ---------------------------------------------------------------------------
    // One case per node
    // ---------------------------------------------------------------------------

    [Fact]
    public void Compile_EqualFilter_EmitsEqualsExpression()
    {
        var filter = SearchFilter.Eq("status", SearchValue.From("active"));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("status = \"active\"");
    }

    [Fact]
    public void Compile_NotEqualFilter_EmitsNotEqualsExpression()
    {
        var filter = SearchFilter.Ne("status", SearchValue.From("archived"));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("status != \"archived\"");
    }

    [Fact]
    public void Compile_InFilter_EmitsInExpression_WithBracketedCommaSeparatedValues()
    {
        var filter = SearchFilter.In("category", SearchValue.From("a"), SearchValue.From("b"));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("category IN [\"a\", \"b\"]");
    }

    [Fact]
    public void Compile_InFilter_WithSingleValue_EmitsSingleElementList()
    {
        var filter = SearchFilter.In("category", SearchValue.From("only"));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("category IN [\"only\"]");
    }

    [Fact]
    public void Compile_RangeFilter_WithBothBoundsInclusive_EmitsGteAndLteJoinedByAnd()
    {
        var filter = SearchFilter.Between("price", SearchValue.From(10L), SearchValue.From(100L));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("(price >= 10) AND (price <= 100)");
    }

    [Fact]
    public void Compile_RangeFilter_WithExclusiveBounds_EmitsGtAndLt()
    {
        var filter = SearchFilter.Between(
            "price", SearchValue.From(10L), SearchValue.From(100L), fromInclusive: false, toInclusive: false);

        MeilisearchFilterCompiler.Compile(filter).Should().Be("(price > 10) AND (price < 100)");
    }

    [Fact]
    public void Compile_RangeFilter_WithOpenLowerBound_EmitsOnlyUpperClause()
    {
        var filter = SearchFilter.Between("price", from: null, to: SearchValue.From(100L));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("price <= 100");
    }

    [Fact]
    public void Compile_RangeFilter_WithOpenUpperBound_EmitsOnlyLowerClause()
    {
        var filter = SearchFilter.Between("price", from: SearchValue.From(10L), to: null);

        MeilisearchFilterCompiler.Compile(filter).Should().Be("price >= 10");
    }

    [Fact]
    public void Compile_ExistsFilter_EmitsExistsExpression()
    {
        var filter = SearchFilter.Exists("optionalField");

        MeilisearchFilterCompiler.Compile(filter).Should().Be("optionalField EXISTS");
    }

    [Fact]
    public void Compile_AndFilter_JoinsOperandsWithAnd_NoParentheses()
    {
        var filter = SearchFilter.All(
            SearchFilter.Eq("a", SearchValue.From(1L)),
            SearchFilter.Eq("b", SearchValue.From(2L)));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("a = 1 AND b = 2");
    }

    [Fact]
    public void Compile_AndFilter_WithNoOperands_EmitsEmptyString()
    {
        var filter = SearchFilter.All();

        MeilisearchFilterCompiler.Compile(filter).Should().BeEmpty();
    }

    [Fact]
    public void Compile_OrFilter_WrapsJoinedOperandsInParentheses()
    {
        var filter = SearchFilter.Any(
            SearchFilter.Eq("a", SearchValue.From(1L)),
            SearchFilter.Eq("b", SearchValue.From(2L)));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("(a = 1 OR b = 2)");
    }

    [Fact]
    public void Compile_OrFilter_WithNoOperands_EmitsEmptyParentheses()
    {
        var filter = SearchFilter.Any();

        MeilisearchFilterCompiler.Compile(filter).Should().Be("()");
    }

    [Fact]
    public void Compile_NotFilter_WrapsOperandInNotParentheses()
    {
        var filter = SearchFilter.Negate(SearchFilter.Eq("status", SearchValue.From("archived")));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("NOT (status = \"archived\")");
    }

    [Fact]
    public void Compile_NotFilter_WrappingOrFilter_NestsBothParenthesesSets()
    {
        var inner = SearchFilter.Any(
            SearchFilter.Eq("a", SearchValue.From(1L)),
            SearchFilter.Eq("b", SearchValue.From(2L)));
        var filter = SearchFilter.Negate(inner);

        MeilisearchFilterCompiler.Compile(filter).Should().Be("NOT ((a = 1 OR b = 2))");
    }

    // ---------------------------------------------------------------------------
    // Value formatting: numerics/booleans bare, strings escaped and quoted, timestamps as epoch seconds
    // ---------------------------------------------------------------------------

    [Fact]
    public void Compile_Int64Value_EmittedBare()
    {
        var filter = SearchFilter.Eq("count", SearchValue.From(42L));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("count = 42");
    }

    [Fact]
    public void Compile_DoubleValue_EmittedBare()
    {
        var filter = SearchFilter.Eq("rating", SearchValue.From(4.5));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("rating = 4.5");
    }

    [Fact]
    public void Compile_TrueBooleanValue_EmittedBareLowercase()
    {
        var filter = SearchFilter.Eq("isActive", SearchValue.From(true));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("isActive = true");
    }

    [Fact]
    public void Compile_FalseBooleanValue_EmittedBareLowercase()
    {
        var filter = SearchFilter.Eq("isActive", SearchValue.From(false));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("isActive = false");
    }

    [Fact]
    public void Compile_DateTimeOffsetValue_EmittedAsUnixEpochSeconds()
    {
        var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var filter = SearchFilter.Eq("createdAt", SearchValue.From(timestamp));

        MeilisearchFilterCompiler.Compile(filter).Should().Be($"createdAt = {timestamp.ToUnixTimeSeconds()}");
    }

    [Fact]
    public void Compile_StringValueContainingDoubleQuote_IsEscaped()
    {
        var filter = SearchFilter.Eq("name", SearchValue.From("say \"hi\""));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("name = \"say \\\"hi\\\"\"");
    }

    [Fact]
    public void Compile_StringValueContainingBackslash_IsEscaped()
    {
        var filter = SearchFilter.Eq("path", SearchValue.From(@"C:\temp"));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("path = \"C:\\\\temp\"");
    }

    [Fact]
    public void Compile_StringValueContainingFilterOperators_IsQuotedVerbatim()
    {
        // Quoting the whole value means the DSL's own operator keywords inside a value never get
        // interpreted as syntax — this is the classic filter-injection guard.
        var filter = SearchFilter.Eq("name", SearchValue.From("a AND b OR c"));

        MeilisearchFilterCompiler.Compile(filter).Should().Be("name = \"a AND b OR c\"");
    }

    // ---------------------------------------------------------------------------
    // TenantScope injection — the outermost AND, regardless of the caller's filter shape
    // ---------------------------------------------------------------------------

    [Fact]
    public void CompileWithTenantScope_WithNoCallerFilter_EmitsOnlyTenantClause()
    {
        var definition = TenantedDefinition();

        var result = MeilisearchFilterCompiler.CompileWithTenantScope(definition, null, TenantScope.Of("tenant-a"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("tenantId = \"tenant-a\"");
    }

    [Fact]
    public void CompileWithTenantScope_WithCallerFilter_PrependsTenantClauseAsOutermostAnd()
    {
        var definition = TenantedDefinition();
        var callerFilter = SearchFilter.Eq("status", SearchValue.From("active"));

        var result = MeilisearchFilterCompiler.CompileWithTenantScope(definition, callerFilter, TenantScope.Of("tenant-a"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("(tenantId = \"tenant-a\") AND (status = \"active\")");
    }

    [Fact]
    public void CompileWithTenantScope_WithComplexCallerFilter_StillPrependsTenantClauseOutermost()
    {
        var definition = TenantedDefinition();
        var callerFilter = SearchFilter.Any(
            SearchFilter.Eq("status", SearchValue.From("active")),
            SearchFilter.Eq("status", SearchValue.From("pending")));

        var result = MeilisearchFilterCompiler.CompileWithTenantScope(definition, callerFilter, TenantScope.Of("tenant-a"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("(tenantId = \"tenant-a\") AND (");
    }

    [Fact]
    public void CompileWithTenantScope_OnUndeclaredTenantField_IgnoresTenantScope()
    {
        var definition = SearchIndexDefinition.Create("products", []).Value; // no TenantField declared

        var result = MeilisearchFilterCompiler.CompileWithTenantScope(definition, null, TenantScope.Of("tenant-a"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void CompileWithTenantScope_OnTenantedIndex_WithTenantScopeNone_FailsClosed_WithTenantScopeMissing()
    {
        var definition = TenantedDefinition();

        var result = MeilisearchFilterCompiler.CompileWithTenantScope(definition, null, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.tenant_scope_missing");
    }

    private static SearchIndexDefinition TenantedDefinition() => new SearchIndexDefinitionBuilder("products")
        .TenantField("tenantId")
        .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
        .Field("status", SearchFieldKind.Keyword, filterable: true)
        .Build()
        .Value;
}
