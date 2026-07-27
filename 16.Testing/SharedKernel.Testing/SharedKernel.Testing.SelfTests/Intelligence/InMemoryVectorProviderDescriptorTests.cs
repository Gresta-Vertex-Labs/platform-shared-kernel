using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves <see cref="InMemoryVectorProviderDescriptor"/> against
/// <c>IVectorProviderDescriptor</c>'s documented zero-I/O pre-flight-validation contract -- no
/// consuming domain has adopted this fake yet, so this self-test is the only behavioral proof today,
/// per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryVectorProviderDescriptorTests
{
    [Fact]
    public void Constructor_Default_UsesInMemoryFakeProviderName_NeverARealEngineName()
    {
        var descriptor = new InMemoryVectorProviderDescriptor();

        Assert.Equal("in-memory-fake", descriptor.ProviderName);
        Assert.NotEqual(IntelligenceWellKnown.QdrantProviderName, descriptor.ProviderName);
    }

    [Fact]
    public void Constructor_CustomProviderName_IsUsed()
    {
        var descriptor = new InMemoryVectorProviderDescriptor("custom-fake");

        Assert.Equal("custom-fake", descriptor.ProviderName);
    }

    [Fact]
    public void Constructor_NullProviderName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryVectorProviderDescriptor(null!));

    [Fact]
    public void Ceilings_DefaultToDocumentedValues()
    {
        var descriptor = new InMemoryVectorProviderDescriptor();

        Assert.Equal(1000, descriptor.MaxBatchSize);
        Assert.Equal(4096, descriptor.MaxVectorDimension);
        Assert.Equal(10, descriptor.MaxFilterDepth);
    }

    [Fact]
    public void RegisterCollection_PopulatesRegisteredCollections()
    {
        var descriptor = new InMemoryVectorProviderDescriptor();

        descriptor.RegisterCollection("chunks", BuildDefinition());

        Assert.Contains("chunks", descriptor.RegisteredCollections);
    }

    [Fact]
    public void Validate_UnregisteredCollection_ReturnsCollectionNotFound()
    {
        var descriptor = new InMemoryVectorProviderDescriptor();

        var result = descriptor.Validate("never-registered", BuildQuery());

        Assert.Equal(IntelligenceErrors.CollectionNotFound("never-registered"), result.Error);
    }

    [Fact]
    public void Validate_VectorDimensionExceedsCeiling_ReturnsInvalidQuery()
    {
        var descriptor = new InMemoryVectorProviderDescriptor { MaxVectorDimension = 2 };
        descriptor.RegisterCollection("chunks", BuildDefinition());

        var result = descriptor.Validate("chunks", BuildQuery(vector: [1f, 0f, 0f]));

        Assert.True(result.IsFailure);
        Assert.Equal("intelligence.invalid_query", result.Error.Code);
    }

    [Fact]
    public void Validate_FilterDepthExceedsCeiling_ReturnsFilterDepthExceeded()
    {
        var descriptor = new InMemoryVectorProviderDescriptor { MaxFilterDepth = 2 };
        descriptor.RegisterCollection("chunks", BuildDefinition());
        var deepFilter = VectorFilter.Negate(VectorFilter.Negate(VectorFilter.Eq("Status", "active")));

        var result = descriptor.Validate("chunks", BuildQuery(filter: deepFilter));

        Assert.Equal(IntelligenceErrors.FilterDepthExceeded(3, 2), result.Error);
    }

    [Fact]
    public void Validate_LegalRequest_Succeeds()
    {
        var descriptor = new InMemoryVectorProviderDescriptor();
        descriptor.RegisterCollection("chunks", BuildDefinition());

        var result = descriptor.Validate("chunks", BuildQuery(filter: VectorFilter.Eq("Status", "active")));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Reset_ClearsRegisteredCollections_ValidateAgainReportsCollectionNotFound()
    {
        var descriptor = new InMemoryVectorProviderDescriptor();
        descriptor.RegisterCollection("chunks", BuildDefinition());

        descriptor.Reset();

        Assert.Empty(descriptor.RegisteredCollections);
        Assert.Equal(IntelligenceErrors.CollectionNotFound("chunks"), descriptor.Validate("chunks", BuildQuery()).Error);
    }

    private static VectorCollectionDefinition BuildDefinition() =>
        VectorCollectionDefinition.Create(
            "chunks", "test-model", 2, VectorDistanceMetric.Cosine,
            [new VectorFieldDefinition { Name = "Status", Kind = VectorFieldKind.String, Filterable = true }]).Value;

    private static VectorQuery BuildQuery(float[]? vector = null, VectorFilter? filter = null) => new()
    {
        Vector = vector ?? [1f, 0f],
        ModelId = "test-model",
        Filter = filter,
    };
}
