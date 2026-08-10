using System.Reflection;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests;

/// <summary>
/// T-08: reflection-based contract shape tests, mirroring the <c>06.Persistence</c>/<c>08.Storage</c>
/// <c>ContractShapeTests</c> precedent. Locks this domain's most novel and most easily-regressed
/// signatures against silent regression: <see cref="ISearchIndex{TDocument}.EnumerateAsync"/> returns
/// a bare <see cref="IAsyncEnumerable{T}"/>; every write member takes a non-optional
/// <see cref="SearchWriteConsistency"/>; every read member and the one filtered write take a
/// non-optional, non-nullable <see cref="TenantScope"/>; <see cref="SearchRequest"/> declares no
/// <c>TenantScope</c> member; <see cref="SearchHit{TDocument}"/> declares no <c>Score</c> member and
/// does declare <c>Rank</c>; <see cref="SearchResults{TDocument}.TotalHits"/> is <see cref="long"/>;
/// <see cref="TotalHitsAccuracy"/> has exactly three members; <see cref="SearchFilter"/> has exactly
/// eight sealed subtypes; and <see cref="ISearchProviderDescriptor"/> declares no capability-flags
/// enum member.
/// </summary>
public sealed class ContractShapeTests
{
    private static readonly Type SearchIndexType = typeof(ISearchIndex<>);

    [Fact]
    public void EnumerateAsync_Returns_BareIAsyncEnumerableOfTDocument()
    {
        var method = SearchIndexType.GetMethod(nameof(ISearchIndex<TestDocument>.EnumerateAsync));

        method.Should().NotBeNull();
        method!.ReturnType.IsGenericType.Should().BeTrue();
        method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
    }

    [Fact]
    public void EnumerateAsync_IsNot_ResultWrapped()
    {
        var method = SearchIndexType.GetMethod(nameof(ISearchIndex<TestDocument>.EnumerateAsync))!;

        method.ReturnType.Name.Should().NotContain(
            "Task", "EnumerateAsync must be the one documented non-Result, non-Task streaming exception in this domain");
    }

    [Theory]
    [InlineData(nameof(ISearchIndex<TestDocument>.IndexAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.IndexManyAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.DeleteAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.DeleteManyAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.DeleteByFilterAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.ClearAsync))]
    public void EveryWriteMember_TakesNonOptional_SearchWriteConsistencyParameter(string memberName)
    {
        // IndexManyAsync/DeleteManyAsync are overloaded (the additive SearchBulkWriteOptions overload,
        // D-33/C-52) — GetMethod(string) throws AmbiguousMatchException for either name, so every
        // overload sharing memberName is checked instead of assuming exactly one match.
        var methods = SearchIndexType.GetMethods().Where(m => m.Name == memberName).ToList();
        methods.Should().NotBeEmpty($"no member named {memberName} was found on ISearchIndex<TDocument>");

        foreach (var method in methods)
        {
            var parameter = method.GetParameters().Single(p => p.ParameterType == typeof(SearchWriteConsistency));

            parameter.IsOptional.Should().BeFalse(
                $"{memberName}'s SearchWriteConsistency parameter must be mandatory and non-defaulted " +
                $"(overload with {method.GetParameters().Length} parameter(s))");
        }
    }

    [Theory]
    [InlineData(nameof(ISearchIndex<TestDocument>.SearchAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.GetAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.CountAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.EnumerateAsync))]
    [InlineData(nameof(ISearchIndex<TestDocument>.DeleteByFilterAsync))]
    public void EveryReadMemberAndTheFilteredWrite_TakesNonOptional_TenantScopeParameter(string memberName)
    {
        var method = SearchIndexType.GetMethod(memberName)!;
        var parameter = method.GetParameters().Single(p => p.ParameterType == typeof(TenantScope));

        parameter.IsOptional.Should().BeFalse(
            $"{memberName}'s TenantScope parameter must be mandatory, non-nullable, and non-defaulted");
        parameter.ParameterType.Should().Be(typeof(TenantScope), "TenantScope is a non-nullable struct — never TenantScope?");
    }

    [Fact]
    public void SearchRequest_DeclaresNo_TenantScopeMember()
    {
        var properties = typeof(SearchRequest).GetProperties().Select(p => p.Name);

        properties.Should().NotContain("TenantScope",
            "TenantScope must never travel through SearchRequest — it is a separate method parameter");
    }

    [Fact]
    public void SearchHit_DeclaresNo_ScoreMember()
    {
        var properties = typeof(SearchHit<TestDocument>).GetProperties().Select(p => p.Name);

        properties.Should().NotContain("Score", "there is deliberately no portable relevance score in this domain");
    }

    [Fact]
    public void SearchHit_Declares_RankMember()
    {
        var rankProperty = typeof(SearchHit<TestDocument>).GetProperty("Rank");

        rankProperty.Should().NotBeNull();
        rankProperty!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void SearchResults_TotalHits_IsLong_NotInt()
    {
        var property = typeof(SearchResults<TestDocument>).GetProperty(nameof(SearchResults<TestDocument>.TotalHits));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(long));
    }

    [Fact]
    public void TotalHitsAccuracy_HasExactlyThreeMembers()
    {
        Enum.GetValues<TotalHitsAccuracy>().Should().HaveCount(3);
    }

    [Fact]
    public void SearchFilter_HasExactlyEightSealedSubtypes()
    {
        var subtypes = typeof(SearchFilter).Assembly.GetTypes()
            .Where(t => t.IsSealed && t.BaseType == typeof(SearchFilter))
            .ToList();

        subtypes.Should().HaveCount(8);
    }

    [Fact]
    public void ISearchProviderDescriptor_DeclaresNoCapabilityFlagsEnumMember()
    {
        var properties = typeof(ISearchProviderDescriptor).GetProperties();

        properties.Should().HaveCount(4, "ProviderName, MaxTotalHits, MaxFacetValues, RegisteredIndexes — and nothing else");
        properties.Should().NotContain(p => p.PropertyType.IsEnum,
            "no property may be a capability-flags enum — the compile-error-on-swap mechanism replaces runtime flag checks");
    }

    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }
}
