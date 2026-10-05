using FluentAssertions;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Diagnostics;

namespace SharedKernel.AI.Qdrant.Tests.Diagnostics;

public sealed class QdrantProviderDescriptorTests
{
    private static QdrantProviderDescriptor CreateDescriptor(int maxVectorDimension = 4096, int maxFilterDepth = 10) =>
        new(
            new Dictionary<string, VectorCollectionDefinition>(StringComparer.Ordinal)
            {
                ["products"] = VectorCollectionDefinition.Create("products", "model-a", 2, VectorDistanceMetric.Cosine, []).Value,
            },
            maxBatchSize: 1000,
            maxVectorDimension: maxVectorDimension,
            maxFilterDepth: maxFilterDepth);

    [Fact]
    public void Validate_UnregisteredCollection_ReturnsCollectionNotFound()
    {
        var descriptor = CreateDescriptor();
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = "model-a" };

        var result = descriptor.Validate("unknown", query);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.collection_not_found");
    }

    [Fact]
    public void Validate_VectorExceedsMaxDimension_ReturnsFailure()
    {
        var descriptor = CreateDescriptor(maxVectorDimension: 2);
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f, 0.3f }, ModelId = "model-a" };

        var result = descriptor.Validate("products", query);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_FilterDepthExceedsCeiling_ReturnsFilterDepthExceeded()
    {
        var descriptor = CreateDescriptor(maxFilterDepth: 1);
        var deepFilter = VectorFilter.Negate(VectorFilter.Negate(VectorFilter.Exists("tags")));
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = "model-a", Filter = deepFilter };

        var result = descriptor.Validate("products", query);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.filter_depth_exceeded");
    }

    [Fact]
    public void Validate_WellFormedQuery_Succeeds()
    {
        var descriptor = CreateDescriptor();
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = "model-a" };

        var result = descriptor.Validate("products", query);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ProviderName_IsQdrant()
    {
        var descriptor = CreateDescriptor();

        descriptor.ProviderName.Should().Be("qdrant");
    }
}
