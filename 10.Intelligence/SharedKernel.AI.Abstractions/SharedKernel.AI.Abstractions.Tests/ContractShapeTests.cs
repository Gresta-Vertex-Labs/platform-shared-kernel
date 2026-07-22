using System.Reflection;
using FluentAssertions;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Tests;

/// <summary>
/// Reflection-based contract shape tests, mirroring the <c>06.Persistence</c>/<c>08.Storage</c>/
/// <c>09.Search</c> <c>ContractShapeTests</c> precedent. Locks this domain's most novel and most
/// easily-regressed signatures against silent regression: <see cref="IVectorCollection{TRecord}.ScrollAsync"/>
/// and <see cref="ISemanticKernel.CompleteStreamingAsync"/> both return a bare
/// <see cref="IAsyncEnumerable{T}"/>; every read member and the filtered write on
/// <see cref="IVectorCollection{TRecord}"/> take a non-optional, non-nullable
/// <see cref="TenantScope"/>; <see cref="VectorQuery"/> declares no <c>TenantScope</c> member;
/// <see cref="VectorHit{TRecord}"/> declares both <c>Score</c> and <c>Rank</c> (the one deliberate
/// deviation from <c>09.Search</c>'s score ban); <see cref="VectorFilter"/> has exactly eight sealed
/// subtypes; and neither provider descriptor declares a capability-flags enum member.
/// </summary>
public sealed class ContractShapeTests
{
    private static readonly Type VectorCollectionType = typeof(IVectorCollection<>);

    [Fact]
    public void ScrollAsync_Returns_BareIAsyncEnumerableOfTRecord()
    {
        var method = VectorCollectionType.GetMethod(nameof(IVectorCollection<TestRecord>.ScrollAsync));

        method.Should().NotBeNull();
        method!.ReturnType.IsGenericType.Should().BeTrue();
        method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(IAsyncEnumerable<>));
        method.ReturnType.Name.Should().NotContain("Task");
    }

    [Fact]
    public void CompleteStreamingAsync_Returns_BareIAsyncEnumerableOfCompletionChunk()
    {
        var method = typeof(ISemanticKernel).GetMethod(nameof(ISemanticKernel.CompleteStreamingAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(IAsyncEnumerable<CompletionChunk>));
        method.ReturnType.Name.Should().NotContain("Task");
    }

    [Theory]
    [InlineData(nameof(IVectorCollection<TestRecord>.UpsertAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.UpsertManyAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.DeleteAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.DeleteManyAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.DeleteByFilterAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.QueryAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.GetAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.CountAsync))]
    [InlineData(nameof(IVectorCollection<TestRecord>.ScrollAsync))]
    public void EveryReadMemberAndEveryFilteredWrite_TakesNonOptional_TenantScopeParameter(string memberName)
    {
        var method = VectorCollectionType.GetMethod(memberName)!;
        var parameter = method.GetParameters().Single(p => p.ParameterType == typeof(TenantScope));

        parameter.IsOptional.Should().BeFalse(
            $"{memberName}'s TenantScope parameter must be mandatory and non-defaulted");
        parameter.ParameterType.Should().Be(typeof(TenantScope), "TenantScope is a non-nullable struct — never TenantScope?");
    }

    [Fact]
    public void VectorQuery_DeclaresNo_TenantScopeMember()
    {
        var properties = typeof(VectorQuery).GetProperties().Select(p => p.Name);

        properties.Should().NotContain("TenantScope",
            "TenantScope must never travel through VectorQuery — it is a separate method parameter");
    }

    [Fact]
    public void VectorHit_Declares_ScoreMember()
    {
        var scoreProperty = typeof(VectorHit<TestRecord>).GetProperty("Score");

        scoreProperty.Should().NotBeNull("Score is the one deliberate deviation from 09.Search's score ban");
        scoreProperty!.PropertyType.Should().Be(typeof(float));
    }

    [Fact]
    public void VectorHit_Declares_RankMember()
    {
        var rankProperty = typeof(VectorHit<TestRecord>).GetProperty("Rank");

        rankProperty.Should().NotBeNull();
        rankProperty!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void VectorFilter_HasExactlyEightSealedSubtypes()
    {
        var subtypes = typeof(VectorFilter).Assembly.GetTypes()
            .Where(t => t.IsSealed && t.BaseType == typeof(VectorFilter))
            .ToList();

        subtypes.Should().HaveCount(8);
    }

    [Fact]
    public void IVectorProviderDescriptor_DeclaresNoCapabilityFlagsEnumMember()
    {
        var properties = typeof(IVectorProviderDescriptor).GetProperties();

        properties.Should().HaveCount(5,
            "ProviderName, MaxBatchSize, MaxVectorDimension, MaxFilterDepth, RegisteredCollections — and nothing else");
        properties.Should().NotContain(p => p.PropertyType.IsEnum,
            "no property may be a capability-flags enum — the compile-error-on-swap mechanism replaces runtime flag checks");
    }

    [Fact]
    public void ICompletionProviderDescriptor_DeclaresNoCapabilityFlagsEnumMember()
    {
        var properties = typeof(ICompletionProviderDescriptor).GetProperties();

        properties.Should().HaveCount(3, "ProviderName, ContextWindowTokens, MaxOutputTokens — and nothing else");
        properties.Should().NotContain(p => p.PropertyType.IsEnum,
            "no capability-flags enum — no SupportsStreaming boolean either");
        properties.Should().NotContain(p => p.PropertyType == typeof(bool),
            "no SupportsStreaming or similar boolean capability flag");
    }

    [Fact]
    public void ISemanticKernel_DeclaresNo_InvokeToolMember()
    {
        var methodNames = typeof(ISemanticKernel).GetMethods().Select(m => m.Name);

        methodNames.Should().NotContain(name => name.Contains("InvokeTool", StringComparison.OrdinalIgnoreCase),
            "tool execution is always consumer-owned — never invoked from this contract");
    }

    private sealed class TestRecord : IVectorRecord
    {
        public required string Id { get; init; }

        public ReadOnlyMemory<float> Vector { get; init; }

        public required string ModelId { get; init; }

        public IReadOnlyDictionary<string, VectorValue> Metadata { get; init; } =
            new Dictionary<string, VectorValue>();
    }
}
