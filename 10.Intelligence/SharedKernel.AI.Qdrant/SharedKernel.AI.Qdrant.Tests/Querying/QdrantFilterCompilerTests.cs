using FluentAssertions;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Querying;

namespace SharedKernel.AI.Qdrant.Tests.Querying;

public sealed class QdrantFilterCompilerTests
{
    [Fact]
    public void Compile_EqualFilter_String_BuildsKeywordMatch()
    {
        var filter = VectorFilter.Eq("status", VectorValue.From("active"));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        compiled.Must.Should().HaveCount(1);
        var condition = compiled.Must[0];
        condition.Field.Key.Should().Be("status");
        condition.Field.Match.Keyword.Should().Be("active");
    }

    [Fact]
    public void Compile_EqualFilter_Double_BuildsDegenerateRange()
    {
        var filter = VectorFilter.Eq("price", VectorValue.From(9.99));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var condition = compiled.Must[0];
        condition.Field.Range.Gte.Should().Be(9.99);
        condition.Field.Range.Lte.Should().Be(9.99);
    }

    [Fact]
    public void Compile_NotEqualFilter_WrapsEqualityInMustNot()
    {
        var filter = VectorFilter.Ne("status", VectorValue.From("archived"));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var wrapper = compiled.Must[0];
        wrapper.Filter.MustNot.Should().HaveCount(1);
        wrapper.Filter.MustNot[0].Field.Match.Keyword.Should().Be("archived");
    }

    [Fact]
    public void Compile_InFilter_UniformStrings_UsesNativeKeywordsMatch()
    {
        var filter = VectorFilter.In("category", VectorValue.From("a"), VectorValue.From("b"));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var condition = compiled.Must[0];
        condition.Field.Match.Keywords.Strings.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void Compile_InFilter_MixedKinds_SynthesizesShouldOfEqualities()
    {
        var filter = VectorFilter.In("flag", VectorValue.From(true), VectorValue.From(1L));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var wrapper = compiled.Must[0];
        wrapper.Filter.Should.Should().HaveCount(2);
    }

    [Fact]
    public void Compile_RangeFilter_Numeric_BuildsRangeWithInclusiveBounds()
    {
        var filter = VectorFilter.Between("price", VectorValue.From(1.0), VectorValue.From(10.0));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var condition = compiled.Must[0];
        condition.Field.Range.Gte.Should().Be(1.0);
        condition.Field.Range.Lte.Should().Be(10.0);
    }

    [Fact]
    public void Compile_RangeFilter_ExclusiveBounds_UsesGtLt()
    {
        var filter = VectorFilter.Between("price", VectorValue.From(1.0), VectorValue.From(10.0), fromInclusive: false, toInclusive: false);

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var condition = compiled.Must[0];
        condition.Field.Range.Gt.Should().Be(1.0);
        condition.Field.Range.Lt.Should().Be(10.0);
    }

    [Fact]
    public void Compile_RangeFilter_DateTimeOffset_BuildsDatetimeRange()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var filter = VectorFilter.Between("createdOn", VectorValue.From(from), VectorValue.From(to));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var condition = compiled.Must[0];
        condition.Field.DatetimeRange.Should().NotBeNull();
        condition.Field.DatetimeRange.Gte.ToDateTimeOffset().Should().Be(from);
        condition.Field.DatetimeRange.Lte.ToDateTimeOffset().Should().Be(to);
    }

    [Fact]
    public void Compile_ExistsFilter_IsIsEmptyNegation_NeverIsNull()
    {
        // IsNullCondition matches only a key that is PRESENT with a JSON null value — it does not
        // match a genuinely absent key, so it would make Exists() vacuously true for every record
        // regardless of whether the field was ever set (a confirmed, fixed real-Qdrant defect).
        // IsEmptyCondition's negation is the correct "field genuinely present with a value" semantics.
        var filter = VectorFilter.Exists("tags");

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var wrapper = compiled.Must[0];
        wrapper.Filter.MustNot.Should().HaveCount(1);
        wrapper.Filter.MustNot[0].IsEmpty.Key.Should().Be("tags");
    }

    [Fact]
    public void Compile_AndFilter_BuildsNestedMust()
    {
        var filter = VectorFilter.All(
            VectorFilter.Eq("a", VectorValue.From("1")),
            VectorFilter.Eq("b", VectorValue.From("2")));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var wrapper = compiled.Must[0];
        wrapper.Filter.Must.Should().HaveCount(2);
    }

    [Fact]
    public void Compile_OrFilter_BuildsNestedShould()
    {
        var filter = VectorFilter.Any(
            VectorFilter.Eq("a", VectorValue.From("1")),
            VectorFilter.Eq("b", VectorValue.From("2")));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var wrapper = compiled.Must[0];
        wrapper.Filter.Should.Should().HaveCount(2);
    }

    [Fact]
    public void Compile_NotFilter_BuildsNestedMustNot()
    {
        var filter = VectorFilter.Negate(VectorFilter.Eq("a", VectorValue.From("1")));

        var compiled = QdrantFilterCompiler.Compile(filter, tenantField: null, TenantScope.None);

        var wrapper = compiled.Must[0];
        wrapper.Filter.MustNot.Should().HaveCount(1);
    }

    [Fact]
    public void Compile_NullFilter_WithTenantField_InjectsOnlyTenantClause()
    {
        var compiled = QdrantFilterCompiler.Compile(null, "tenantId", TenantScope.Of("tenant-a"));

        compiled.Must.Should().HaveCount(1);
        compiled.Must[0].Field.Key.Should().Be("tenantId");
        compiled.Must[0].Field.Match.Keyword.Should().Be("tenant-a");
    }

    [Fact]
    public void Compile_WithFilterAndTenantField_InjectsTenantAsOutermostConjunctionAfterCallerFilter()
    {
        var filter = VectorFilter.Eq("status", VectorValue.From("active"));

        var compiled = QdrantFilterCompiler.Compile(filter, "tenantId", TenantScope.Of("tenant-a"));

        compiled.Must.Should().HaveCount(2);
        compiled.Must[0].Field.Key.Should().Be("status");
        compiled.Must[1].Field.Key.Should().Be("tenantId");
        compiled.Must[1].Field.Match.Keyword.Should().Be("tenant-a");
    }

    [Fact]
    public void Compile_TenantScopeNone_WithNoTenantField_DoesNotInjectTenantClause()
    {
        var compiled = QdrantFilterCompiler.Compile(null, tenantField: null, TenantScope.None);

        compiled.Must.Should().BeEmpty();
    }
}
