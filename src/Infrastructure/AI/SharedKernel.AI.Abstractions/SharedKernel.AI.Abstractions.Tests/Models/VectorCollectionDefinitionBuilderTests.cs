using FluentAssertions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Tests.Models;

/// <summary>
/// <see cref="VectorCollectionDefinitionBuilder"/> fluent builder tests — every method returns a new
/// builder reference (fluent chaining), <see cref="VectorCollectionDefinitionBuilder.Build"/> requires
/// <c>EmbeddingModel(...)</c> and <c>DistanceMetric(...)</c> to have been called, and a
/// <c>TenantField</c> naming a non-filterable (or undeclared) field fails.
/// </summary>
public sealed class VectorCollectionDefinitionBuilderTests
{
    [Fact]
    public void Build_WithoutEmbeddingModel_ReturnsValidationFailure()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Build_WithoutDistanceMetric_ReturnsValidationFailure()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("text-embedding-3-small", 1536)
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Build_WithEmbeddingModelAndDistanceMetric_Succeeds()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("text-embedding-3-small", 1536)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("documents");
        result.Value.EmbeddingModelId.Should().Be("text-embedding-3-small");
        result.Value.Dimension.Should().Be(1536);
        result.Value.DistanceMetric.Should().Be(VectorDistanceMetric.Cosine);
    }

    [Fact]
    public void Build_WithFields_PopulatesFields()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("model", 128)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field("source", VectorFieldKind.String, filterable: true)
            .Field("chunkIndex", VectorFieldKind.Int64)
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.Fields.Should().HaveCount(2);
        result.Value.Fields[0].Name.Should().Be("source");
        result.Value.Fields[0].Filterable.Should().BeTrue();
        result.Value.Fields[1].Filterable.Should().BeFalse();
    }

    [Fact]
    public void Build_WithTenantFieldNamingAFilterableField_Succeeds()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("model", 128)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .TenantField("tenantId")
            .Field("tenantId", VectorFieldKind.String, filterable: true)
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantField.Should().Be("tenantId");
    }

    [Fact]
    public void Build_WithTenantFieldNamingAnUndeclaredField_ReturnsValidationFailure()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("model", 128)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .TenantField("tenantId")
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Build_WithTenantFieldNamingANonFilterableField_ReturnsValidationFailure()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("model", 128)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .TenantField("tenantId")
            .Field("tenantId", VectorFieldKind.String, filterable: false)
            .Build();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Build_WithDuplicateFieldNames_ReturnsValidationFailure()
    {
        var result = new VectorCollectionDefinitionBuilder("documents")
            .EmbeddingModel("model", 128)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field("f", VectorFieldKind.String)
            .Field("f", VectorFieldKind.Int64)
            .Build();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullOrWhitespaceName_ThrowsArgumentException()
    {
        var act = () => new VectorCollectionDefinitionBuilder("   ");

        act.Should().Throw<ArgumentException>();
    }
}
