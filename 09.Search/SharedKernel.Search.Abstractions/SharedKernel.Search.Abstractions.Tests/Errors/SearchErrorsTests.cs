using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.Abstractions.Tests.Errors;

/// <summary>
/// T-01: <see cref="SearchErrors"/> factory tests — every member returns the expected
/// <see cref="ErrorType"/> and a dot-separated-lowercase code, and the factory-choice tally is
/// asserted explicitly against the live <see cref="Error"/> API: <see cref="ErrorType.NotFound"/> x2,
/// <see cref="ErrorType.Validation"/> x12, <see cref="ErrorType.Conflict"/> x4,
/// <see cref="ErrorType.Unauthorized"/> x2, <see cref="ErrorType.Unexpected"/> x6,
/// <see cref="ErrorType.Unavailable"/> x1, <see cref="ErrorType.Timeout"/> x1,
/// <see cref="ErrorType.BusinessRule"/> x0.
/// </summary>
public sealed class SearchErrorsTests
{
    // ---------------------------------------------------------------------------
    // Error.NotFound (x2)
    // ---------------------------------------------------------------------------

    [Fact]
    public void IndexNotFound_ReturnsNotFound_WithIndexNameInMessage()
    {
        var error = SearchErrors.IndexNotFound("my-index");

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("search.index_not_found");
        error.Message.Should().Contain("my-index");
    }

    [Fact]
    public void DocumentNotFound_ReturnsNotFound_WithIndexNameAndDocumentIdInMessage()
    {
        var error = SearchErrors.DocumentNotFound("my-index", "doc-1");

        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("search.document_not_found");
        error.Message.Should().Contain("my-index").And.Contain("doc-1");
    }

    // ---------------------------------------------------------------------------
    // Error.Validation (x12)
    // ---------------------------------------------------------------------------

    [Fact]
    public void InvalidSearchRequest_ReturnsValidation()
    {
        var error = SearchErrors.InvalidSearchRequest("reason");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.invalid_request");
        error.Message.Should().Contain("reason");
    }

    [Fact]
    public void InvalidFilter_ReturnsValidation()
    {
        var error = SearchErrors.InvalidFilter("reason");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.invalid_filter");
    }

    [Fact]
    public void InvalidIndexDefinition_ReturnsValidation()
    {
        var error = SearchErrors.InvalidIndexDefinition("reason");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void InvalidDocumentId_ReturnsValidation_WithDocumentIdInMessage()
    {
        var error = SearchErrors.InvalidDocumentId("bad id!");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.invalid_document_id");
        error.Message.Should().Contain("bad id!");
    }

    [Fact]
    public void FieldNotSearchable_ReturnsValidation()
    {
        var error = SearchErrors.FieldNotSearchable("my-index", "field");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.field_not_searchable");
        error.Message.Should().Contain("my-index").And.Contain("field");
    }

    [Fact]
    public void FieldNotFilterable_ReturnsValidation()
    {
        var error = SearchErrors.FieldNotFilterable("my-index", "field");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.field_not_filterable");
    }

    [Fact]
    public void FieldNotSortable_ReturnsValidation()
    {
        var error = SearchErrors.FieldNotSortable("my-index", "field");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.field_not_sortable");
    }

    [Fact]
    public void FieldNotFacetable_ReturnsValidation()
    {
        var error = SearchErrors.FieldNotFacetable("my-index", "field");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.field_not_facetable");
    }

    [Fact]
    public void PaginationLimitExceeded_ReturnsValidation_NamingCursorSearchAndEnumerateAsyncAsAlternatives()
    {
        var error = SearchErrors.PaginationLimitExceeded(51, 20, 1000, "meilisearch");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.pagination_limit_exceeded");
        error.Message.Should().Contain("ICursorSearch").And.Contain("EnumerateAsync");
    }

    [Fact]
    public void FacetLimitExceeded_ReturnsValidation()
    {
        var error = SearchErrors.FacetLimitExceeded(150, 100);

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.facet_limit_exceeded");
    }

    [Fact]
    public void TotalHitsNotExact_ReturnsValidation_NamingRequireExactTotalHitsAsRemedy()
    {
        var error = SearchErrors.TotalHitsNotExact();

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.total_hits_not_exact");
        error.Message.Should().Contain("RequireExactTotalHits");
    }

    [Fact]
    public void UnsupportedCapability_ReturnsValidation()
    {
        var error = SearchErrors.UnsupportedCapability("geo_radius", "meilisearch");

        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("search.unsupported_capability");
    }

    // ---------------------------------------------------------------------------
    // Error.Conflict (x4)
    // ---------------------------------------------------------------------------

    [Fact]
    public void IndexAlreadyExists_ReturnsConflict()
    {
        var error = SearchErrors.IndexAlreadyExists("my-index");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("search.index_already_exists");
    }

    [Fact]
    public void IndexDefinitionConflict_ReturnsConflict_NamingCutoverAsRemedy()
    {
        var error = SearchErrors.IndexDefinitionConflict("my-index", "field");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("search.index_definition_conflict");
        error.Message.Should().Contain("CutoverAsync");
    }

    [Fact]
    public void CutoverFailed_ReturnsConflict()
    {
        var error = SearchErrors.CutoverFailed("staging", "live", "reason");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("search.cutover_failed");
    }

    [Fact]
    public void SchemaFingerprintMismatch_ReturnsConflict()
    {
        var error = SearchErrors.SchemaFingerprintMismatch("my-index", "abc", "def");

        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("search.schema_fingerprint_mismatch");
        error.Message.Should().Contain("abc").And.Contain("def");
    }

    // ---------------------------------------------------------------------------
    // Error.Unauthorized (x2)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Unauthorized_ReturnsUnauthorized()
    {
        var error = SearchErrors.Unauthorized("my-index", "search");

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("search.unauthorized");
    }

    [Fact]
    public void TenantScopeMissing_ReturnsUnauthorized_NotBusinessRule()
    {
        var error = SearchErrors.TenantScopeMissing("my-index");

        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Code.Should().Be("search.tenant_scope_missing");
    }

    // ---------------------------------------------------------------------------
    // Error.Unavailable (x1) and Error.Timeout (x1) — P-562: an engine that is down or too slow is
    // retryable, so it reaches the HTTP boundary as 503/504, not 500. Codes unchanged.
    // ---------------------------------------------------------------------------

    [Fact]
    public void Unreachable_ReturnsUnavailable()
    {
        var error = SearchErrors.Unreachable("meilisearch", "http://localhost:7700");

        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be("search.unreachable");
        error.Message.Should().Contain("meilisearch").And.Contain("http://localhost:7700");
    }

    [Fact]
    public void Timeout_ReturnsTimeout()
    {
        var error = SearchErrors.Timeout("search", TimeSpan.FromSeconds(30));

        error.Type.Should().Be(ErrorType.Timeout);
        error.Code.Should().Be("search.timeout");
        error.Message.Should().Contain("search");
    }

    // ---------------------------------------------------------------------------
    // Error.Unexpected (x6)
    // ---------------------------------------------------------------------------

    [Fact]
    public void WriteRejected_ReturnsUnexpected()
    {
        var error = SearchErrors.WriteRejected("my-index", "reason");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("search.write_rejected");
    }

    [Fact]
    public void WriteTimeout_ReturnsTimeout_StatingWriteMayStillLand()
    {
        var error = SearchErrors.WriteTimeout("my-index", TimeSpan.FromSeconds(120));

        error.Type.Should().Be(ErrorType.Timeout);
        error.Code.Should().Be("search.write_timeout");
        error.Message.Should().Contain("may still land");
    }

    [Fact]
    public void BulkPartiallyFailed_ReturnsUnexpected()
    {
        var error = SearchErrors.BulkPartiallyFailed(3, 10);

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("search.bulk_partially_failed");
    }

    [Fact]
    public void ProbeFailed_ReturnsUnexpected()
    {
        var error = SearchErrors.ProbeFailed("my-index", "reason");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("search.probe_failed");
    }

    [Fact]
    public void EngineVersionUnsupported_ReturnsUnexpected()
    {
        var error = SearchErrors.EngineVersionUnsupported("8.6.1", "9.x-10.x");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("search.engine_version_unsupported");
    }

    [Fact]
    public void EngineFault_ReturnsUnexpected()
    {
        var error = SearchErrors.EngineFault("elasticsearch", "search", "detail");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("search.engine_fault");
    }

    // ---------------------------------------------------------------------------
    // Whole-catalog tally — the load-bearing assertion in this file
    // ---------------------------------------------------------------------------

    [Fact]
    public void FullCatalog_MatchesTheDocumentedFactoryTally_AndUsesOnlyRealErrorTypes()
    {
        Error[] errors =
        [
            SearchErrors.IndexNotFound("i"),
            SearchErrors.DocumentNotFound("i", "d"),
            SearchErrors.InvalidSearchRequest("r"),
            SearchErrors.InvalidFilter("r"),
            SearchErrors.InvalidIndexDefinition("r"),
            SearchErrors.InvalidDocumentId("d"),
            SearchErrors.FieldNotSearchable("i", "f"),
            SearchErrors.FieldNotFilterable("i", "f"),
            SearchErrors.FieldNotSortable("i", "f"),
            SearchErrors.FieldNotFacetable("i", "f"),
            SearchErrors.PaginationLimitExceeded(1, 1, 1, "p"),
            SearchErrors.FacetLimitExceeded(1, 1),
            SearchErrors.TotalHitsNotExact(),
            SearchErrors.UnsupportedCapability("c", "p"),
            SearchErrors.IndexAlreadyExists("i"),
            SearchErrors.IndexDefinitionConflict("i", "f"),
            SearchErrors.CutoverFailed("s", "l", "r"),
            SearchErrors.SchemaFingerprintMismatch("i", "e", "a"),
            SearchErrors.Unauthorized("i", "o"),
            SearchErrors.TenantScopeMissing("i"),
            SearchErrors.Unreachable("p", "e"),
            SearchErrors.Timeout("o", TimeSpan.Zero),
            SearchErrors.WriteRejected("i", "r"),
            SearchErrors.WriteTimeout("i", TimeSpan.Zero),
            SearchErrors.BulkPartiallyFailed(1, 2),
            SearchErrors.ProbeFailed("i", "r"),
            SearchErrors.EngineVersionUnsupported("a", "s"),
            SearchErrors.EngineFault("p", "o", "d"),
        ];

        errors.Should().HaveCount(28);
        errors.Count(e => e.Type == ErrorType.NotFound).Should().Be(2);
        errors.Count(e => e.Type == ErrorType.Validation).Should().Be(12);
        errors.Count(e => e.Type == ErrorType.Conflict).Should().Be(4);
        errors.Count(e => e.Type == ErrorType.Unauthorized).Should().Be(2);
        errors.Count(e => e.Type == ErrorType.Unexpected).Should().Be(5);
        errors.Count(e => e.Type == ErrorType.Unavailable).Should().Be(1);
        errors.Count(e => e.Type == ErrorType.Timeout).Should().Be(2);
        errors.Count(e => e.Type == ErrorType.BusinessRule).Should().Be(0);
        errors.Count(e => e.Type == ErrorType.None).Should().Be(0);

        errors.Should().OnlyContain(e => e != Error.None);
        errors.Select(e => e.Code).Should().OnlyContain(c => c.StartsWith("search.", StringComparison.Ordinal));
        errors.Select(e => e.Code).Should().OnlyHaveUniqueItems();
    }
}
