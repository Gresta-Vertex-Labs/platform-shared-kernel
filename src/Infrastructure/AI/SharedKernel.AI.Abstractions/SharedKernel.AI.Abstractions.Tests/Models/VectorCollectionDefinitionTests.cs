using FluentAssertions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Tests.Models;

/// <summary>
/// <see cref="VectorCollectionDefinition.Create"/> validation tests and
/// <see cref="VectorCollectionDefinition.Fingerprint"/> canonicalisation tests — declaration-order
/// independence (ordinal field sort) and sensitivity to every individual model/dimension/metric/
/// tenant-field/field-role change.
/// </summary>
public sealed class VectorCollectionDefinitionTests
{
    // ---------------------------------------------------------------------------
    // Create validation
    // ---------------------------------------------------------------------------

    [Fact]
    public void Create_WithValidInput_Succeeds()
    {
        var result = VectorCollectionDefinition.Create(
            "documents", "text-embedding-3-small", 1536, VectorDistanceMetric.Cosine, []);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("documents");
        result.Value.EmbeddingModelId.Should().Be("text-embedding-3-small");
        result.Value.Dimension.Should().Be(1536);
        result.Value.DistanceMetric.Should().Be(VectorDistanceMetric.Cosine);
    }

    [Fact]
    public void Create_WithEmptyName_ReturnsValidationFailure()
    {
        var result = VectorCollectionDefinition.Create(
            "", "model", 128, VectorDistanceMetric.Cosine, []);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("intelligence.invalid_collection_definition");
    }

    [Fact]
    public void Create_WithEmptyModelId_ReturnsValidationFailure()
    {
        var result = VectorCollectionDefinition.Create(
            "documents", "", 128, VectorDistanceMetric.Cosine, []);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveDimension_ReturnsValidationFailure(int dimension)
    {
        var result = VectorCollectionDefinition.Create(
            "documents", "model", dimension, VectorDistanceMetric.Cosine, []);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_WithDuplicateFieldNames_ReturnsValidationFailure()
    {
        var result = VectorCollectionDefinition.Create(
            "documents", "model", 128, VectorDistanceMetric.Cosine,
            [
                new VectorFieldDefinition { Name = "tenantId", Kind = VectorFieldKind.String },
                new VectorFieldDefinition { Name = "tenantId", Kind = VectorFieldKind.String },
            ]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.invalid_collection_definition");
    }

    [Fact]
    public void Create_WithEmptyFieldName_ReturnsValidationFailure()
    {
        var result = VectorCollectionDefinition.Create(
            "documents", "model", 128, VectorDistanceMetric.Cosine,
            [new VectorFieldDefinition { Name = "", Kind = VectorFieldKind.String }]);

        result.IsFailure.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // Fingerprint
    // ---------------------------------------------------------------------------

    private static VectorCollectionDefinition BaselineDefinition(
        IReadOnlyList<VectorFieldDefinition>? fieldsInDeclarationOrder = null) => new()
    {
        Name = "documents",
        EmbeddingModelId = "text-embedding-3-small",
        Dimension = 1536,
        DistanceMetric = VectorDistanceMetric.Cosine,
        TenantField = "tenantId",
        Fields = fieldsInDeclarationOrder ??
        [
            new VectorFieldDefinition { Name = "source", Kind = VectorFieldKind.String, Filterable = true },
            new VectorFieldDefinition { Name = "chunkIndex", Kind = VectorFieldKind.Int64, Filterable = true },
            new VectorFieldDefinition { Name = "tenantId", Kind = VectorFieldKind.String, Filterable = true },
        ],
    };

    [Fact]
    public void Fingerprint_Is64LowercaseHexCharacters()
    {
        var fingerprint = BaselineDefinition().Fingerprint;

        fingerprint.Should().HaveLength(64);
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Fingerprint_IsDeterministic_ForIdenticalInputs()
    {
        var first = BaselineDefinition().Fingerprint;
        var second = BaselineDefinition().Fingerprint;

        first.Should().Be(second);
    }

    [Fact]
    public void Fingerprint_IsStable_AcrossFieldDeclarationOrder()
    {
        var declaredSourceFirst = BaselineDefinition(
        [
            new VectorFieldDefinition { Name = "source", Kind = VectorFieldKind.String, Filterable = true },
            new VectorFieldDefinition { Name = "chunkIndex", Kind = VectorFieldKind.Int64, Filterable = true },
            new VectorFieldDefinition { Name = "tenantId", Kind = VectorFieldKind.String, Filterable = true },
        ]);

        var declaredTenantFirst = BaselineDefinition(
        [
            new VectorFieldDefinition { Name = "tenantId", Kind = VectorFieldKind.String, Filterable = true },
            new VectorFieldDefinition { Name = "chunkIndex", Kind = VectorFieldKind.Int64, Filterable = true },
            new VectorFieldDefinition { Name = "source", Kind = VectorFieldKind.String, Filterable = true },
        ]);

        declaredSourceFirst.Fingerprint.Should().Be(declaredTenantFirst.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenEmbeddingModelIdChanges()
    {
        var modelA = BaselineDefinition() with { EmbeddingModelId = "model-a" };
        var modelB = BaselineDefinition() with { EmbeddingModelId = "model-b" };

        modelA.Fingerprint.Should().NotBe(modelB.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenDimensionChanges()
    {
        var dim1536 = BaselineDefinition() with { Dimension = 1536 };
        var dim768 = BaselineDefinition() with { Dimension = 768 };

        dim1536.Fingerprint.Should().NotBe(dim768.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenDistanceMetricChanges()
    {
        var cosine = BaselineDefinition() with { DistanceMetric = VectorDistanceMetric.Cosine };
        var euclidean = BaselineDefinition() with { DistanceMetric = VectorDistanceMetric.Euclidean };

        cosine.Fingerprint.Should().NotBe(euclidean.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenTenantFieldChanges()
    {
        var withTenant = BaselineDefinition() with { TenantField = "tenantId" };
        var withoutTenant = BaselineDefinition() with { TenantField = null };
        var differentTenant = BaselineDefinition() with { TenantField = "orgId" };

        withTenant.Fingerprint.Should().NotBe(withoutTenant.Fingerprint);
        withTenant.Fingerprint.Should().NotBe(differentTenant.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenAFieldRoleBooleanChanges()
    {
        var withFilterable = BaselineDefinition() with
        {
            Fields = [new VectorFieldDefinition { Name = "f", Kind = VectorFieldKind.String, Filterable = true }],
        };
        var withoutFilterable = BaselineDefinition() with
        {
            Fields = [new VectorFieldDefinition { Name = "f", Kind = VectorFieldKind.String, Filterable = false }],
        };

        withFilterable.Fingerprint.Should().NotBe(withoutFilterable.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenAFieldKindChanges()
    {
        var asString = BaselineDefinition() with
        {
            Fields = [new VectorFieldDefinition { Name = "f", Kind = VectorFieldKind.String }],
        };
        var asInt64 = BaselineDefinition() with
        {
            Fields = [new VectorFieldDefinition { Name = "f", Kind = VectorFieldKind.Int64 }],
        };

        asString.Fingerprint.Should().NotBe(asInt64.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenAFieldNameChanges()
    {
        var fieldA = BaselineDefinition() with
        {
            Fields = [new VectorFieldDefinition { Name = "a", Kind = VectorFieldKind.String }],
        };
        var fieldB = BaselineDefinition() with
        {
            Fields = [new VectorFieldDefinition { Name = "b", Kind = VectorFieldKind.String }],
        };

        fieldA.Fingerprint.Should().NotBe(fieldB.Fingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenCollectionNameChanges()
    {
        var documents = BaselineDefinition() with { Name = "documents" };
        var chunks = BaselineDefinition() with { Name = "chunks" };

        documents.Fingerprint.Should().NotBe(chunks.Fingerprint);
    }
}
