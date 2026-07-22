using FluentAssertions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Tests.Errors;

/// <summary>
/// <see cref="IntelligenceErrors"/> factory tests — every member returns the expected
/// <see cref="ErrorType"/> and a dot-separated-lowercase code, and the factory-choice tally is
/// asserted explicitly against the live <see cref="Error"/> API: <see cref="ErrorType.NotFound"/> x3,
/// <see cref="ErrorType.Validation"/> x12, <see cref="ErrorType.Conflict"/> x4,
/// <see cref="ErrorType.Unauthorized"/> x2, <see cref="ErrorType.Unexpected"/> x10,
/// <see cref="ErrorType.BusinessRule"/> x0.
/// </summary>
public sealed class IntelligenceErrorsTests
{
    // ---------------------------------------------------------------------------
    // Error.NotFound (x3)
    // ---------------------------------------------------------------------------

    [Fact]
    public void CollectionNotFound_ReturnsNotFound()
    {
        var error = IntelligenceErrors.CollectionNotFound("documents");

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("intelligence.collection_not_found");
        error.Message.Should().Contain("documents");
    }

    [Fact]
    public void RecordNotFound_ReturnsNotFound()
    {
        var error = IntelligenceErrors.RecordNotFound("documents", "rec-1");

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("intelligence.record_not_found");
        error.Message.Should().Contain("documents").And.Contain("rec-1");
    }

    [Fact]
    public void ModelNotFound_ReturnsNotFound()
    {
        var error = IntelligenceErrors.ModelNotFound("text-embedding-3-small");

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("intelligence.model_not_found");
    }

    // ---------------------------------------------------------------------------
    // Error.Validation (x12)
    // ---------------------------------------------------------------------------

    [Fact]
    public void InvalidQuery_ReturnsValidation()
    {
        var error = IntelligenceErrors.InvalidQuery("reason");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.invalid_query");
    }

    [Fact]
    public void InvalidFilter_ReturnsValidation()
    {
        var error = IntelligenceErrors.InvalidFilter("reason");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.invalid_filter");
    }

    [Fact]
    public void InvalidCollectionDefinition_ReturnsValidation()
    {
        var error = IntelligenceErrors.InvalidCollectionDefinition("reason");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.invalid_collection_definition");
    }

    [Fact]
    public void InvalidRecordId_ReturnsValidation()
    {
        var error = IntelligenceErrors.InvalidRecordId("bad id!");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.invalid_record_id");
    }

    [Fact]
    public void FieldNotFilterable_ReturnsValidation()
    {
        var error = IntelligenceErrors.FieldNotFilterable("documents", "field");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.field_not_filterable");
    }

    [Fact]
    public void EmbeddingModelMismatch_ReturnsValidation()
    {
        var error = IntelligenceErrors.EmbeddingModelMismatch("documents", "model-a", "model-b");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.embedding_model_mismatch");
        error.Message.Should().Contain("model-a").And.Contain("model-b");
    }

    [Fact]
    public void DimensionMismatch_ReturnsValidation()
    {
        var error = IntelligenceErrors.DimensionMismatch("documents", 1536, 768);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.dimension_mismatch");
    }

    [Fact]
    public void DistanceMetricMismatch_ReturnsValidation()
    {
        var error = IntelligenceErrors.DistanceMetricMismatch("documents", "Cosine", "Euclidean");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.distance_metric_mismatch");
    }

    [Fact]
    public void BatchSizeExceeded_ReturnsValidation()
    {
        var error = IntelligenceErrors.BatchSizeExceeded(2000, 1000, "qdrant");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.batch_size_exceeded");
    }

    [Fact]
    public void FilterDepthExceeded_ReturnsValidation()
    {
        var error = IntelligenceErrors.FilterDepthExceeded(12, 10);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.filter_depth_exceeded");
    }

    [Fact]
    public void ContextWindowExceeded_ReturnsValidation()
    {
        var error = IntelligenceErrors.ContextWindowExceeded(128000, 130000);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.context_window_exceeded");
    }

    [Fact]
    public void UnsupportedCapability_ReturnsValidation()
    {
        var error = IntelligenceErrors.UnsupportedCapability("sparse_vectors", "milvus");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("intelligence.unsupported_capability");
    }

    // ---------------------------------------------------------------------------
    // Error.Conflict (x4)
    // ---------------------------------------------------------------------------

    [Fact]
    public void CollectionAlreadyExists_ReturnsConflict()
    {
        var error = IntelligenceErrors.CollectionAlreadyExists("documents");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("intelligence.collection_already_exists");
    }

    [Fact]
    public void CollectionDefinitionConflict_ReturnsConflict_NamingCutoverAsRemedy()
    {
        var error = IntelligenceErrors.CollectionDefinitionConflict("documents", "field");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("intelligence.collection_definition_conflict");
        error.Message.Should().Contain("CutoverAsync");
    }

    [Fact]
    public void CutoverFailed_ReturnsConflict()
    {
        var error = IntelligenceErrors.CutoverFailed("staging", "live", "reason");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("intelligence.cutover_failed");
    }

    [Fact]
    public void SchemaFingerprintMismatch_ReturnsConflict()
    {
        var error = IntelligenceErrors.SchemaFingerprintMismatch("documents", "abc", "def");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("intelligence.schema_fingerprint_mismatch");
    }

    // ---------------------------------------------------------------------------
    // Error.Unauthorized (x2)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Unauthorized_ReturnsUnauthorized()
    {
        var error = IntelligenceErrors.Unauthorized("documents", "query");

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("intelligence.unauthorized");
    }

    [Fact]
    public void TenantScopeMissing_ReturnsUnauthorized_NotBusinessRule()
    {
        var error = IntelligenceErrors.TenantScopeMissing("documents");

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("intelligence.tenant_scope_missing");
    }

    // ---------------------------------------------------------------------------
    // Error.Unexpected (x10)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Unreachable_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.Unreachable("qdrant", "http://localhost:6334");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.unreachable");
    }

    [Fact]
    public void Timeout_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.Timeout("query", TimeSpan.FromSeconds(30));

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.timeout");
    }

    [Fact]
    public void WriteRejected_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.WriteRejected("documents", "reason");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.write_rejected");
    }

    [Fact]
    public void WriteTimeout_ReturnsUnexpected_StatingWriteMayStillLand()
    {
        var error = IntelligenceErrors.WriteTimeout("documents", TimeSpan.FromSeconds(120));

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.write_timeout");
        error.Message.Should().Contain("may still land");
    }

    [Fact]
    public void BulkPartiallyFailed_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.BulkPartiallyFailed(3, 10);

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.bulk_partially_failed");
    }

    [Fact]
    public void ProbeFailed_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.ProbeFailed("documents", "reason");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.probe_failed");
    }

    [Fact]
    public void EngineVersionUnsupported_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.EngineVersionUnsupported("1.9.0", "1.10.x-1.18.x");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.engine_version_unsupported");
    }

    [Fact]
    public void EngineFault_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.EngineFault("qdrant", "upsert", "detail");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.engine_fault");
    }

    [Fact]
    public void RateLimited_ReturnsUnexpected()
    {
        var withRetryAfter = IntelligenceErrors.RateLimited("semantickernel", TimeSpan.FromSeconds(5));
        var withoutRetryAfter = IntelligenceErrors.RateLimited("semantickernel", null);

        withRetryAfter.Type.Should().Be(ErrorType.Unexpected);
        withRetryAfter.Code.Should().Be("intelligence.rate_limited");
        withoutRetryAfter.Code.Should().Be("intelligence.rate_limited");
    }

    [Fact]
    public void CompletionFailed_ReturnsUnexpected()
    {
        var error = IntelligenceErrors.CompletionFailed("semantickernel", "reason");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.completion_failed");
    }

    // ---------------------------------------------------------------------------
    // Whole-catalog tally — the load-bearing assertion in this file
    // ---------------------------------------------------------------------------

    [Fact]
    public void FullCatalog_MatchesTheDocumentedFactoryTally_AndUsesOnlyRealErrorTypes()
    {
        Error[] errors =
        [
            IntelligenceErrors.CollectionNotFound("c"),
            IntelligenceErrors.RecordNotFound("c", "r"),
            IntelligenceErrors.ModelNotFound("m"),
            IntelligenceErrors.InvalidQuery("r"),
            IntelligenceErrors.InvalidFilter("r"),
            IntelligenceErrors.InvalidCollectionDefinition("r"),
            IntelligenceErrors.InvalidRecordId("r"),
            IntelligenceErrors.FieldNotFilterable("c", "f"),
            IntelligenceErrors.EmbeddingModelMismatch("c", "e", "a"),
            IntelligenceErrors.DimensionMismatch("c", 1, 2),
            IntelligenceErrors.DistanceMetricMismatch("c", "e", "a"),
            IntelligenceErrors.BatchSizeExceeded(1, 1, "p"),
            IntelligenceErrors.FilterDepthExceeded(1, 1),
            IntelligenceErrors.ContextWindowExceeded(1, 1),
            IntelligenceErrors.UnsupportedCapability("c", "p"),
            IntelligenceErrors.CollectionAlreadyExists("c"),
            IntelligenceErrors.CollectionDefinitionConflict("c", "f"),
            IntelligenceErrors.CutoverFailed("s", "l", "r"),
            IntelligenceErrors.SchemaFingerprintMismatch("c", "e", "a"),
            IntelligenceErrors.Unauthorized("c", "o"),
            IntelligenceErrors.TenantScopeMissing("c"),
            IntelligenceErrors.Unreachable("p", "e"),
            IntelligenceErrors.Timeout("o", TimeSpan.Zero),
            IntelligenceErrors.WriteRejected("c", "r"),
            IntelligenceErrors.WriteTimeout("c", TimeSpan.Zero),
            IntelligenceErrors.BulkPartiallyFailed(1, 2),
            IntelligenceErrors.ProbeFailed("c", "r"),
            IntelligenceErrors.EngineVersionUnsupported("a", "s"),
            IntelligenceErrors.EngineFault("p", "o", "d"),
            IntelligenceErrors.RateLimited("p", null),
            IntelligenceErrors.CompletionFailed("p", "r"),
        ];

        errors.Should().HaveCount(31);
        errors.Count(e => e.Type == ErrorType.NotFound).Should().Be(3);
        errors.Count(e => e.Type == ErrorType.Validation).Should().Be(12);
        errors.Count(e => e.Type == ErrorType.Conflict).Should().Be(4);
        errors.Count(e => e.Type == ErrorType.Unauthorized).Should().Be(2);
        errors.Count(e => e.Type == ErrorType.Unexpected).Should().Be(10);
        errors.Count(e => e.Type == ErrorType.BusinessRule).Should().Be(0);
        errors.Count(e => e.Type == ErrorType.None).Should().Be(0);

        errors.Should().OnlyContain(e => e != Error.None);
        errors.Select(e => e.Code).Should().OnlyContain(c => c.StartsWith("intelligence.", StringComparison.Ordinal));
        errors.Select(e => e.Code).Should().OnlyHaveUniqueItems();
    }
}
