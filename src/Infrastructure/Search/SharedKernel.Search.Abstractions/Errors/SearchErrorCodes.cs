namespace SharedKernel.Search.Abstractions.Errors;

/// <summary>
/// The stable, wire-visible codes of the neutral <c>09.Search</c> failures, lowercase <c>search.{snake_reason}</c>. Both
/// providers and the in-memory double return exactly these, so a caller branches on a code once and keeps working after
/// a provider swap. The <see cref="SharedKernel.Primitives.Errors.ErrorType"/> of each code is fixed by
/// <see cref="SearchErrors"/>; provider-exclusive codes live in the provider packages
/// (<c>search.elasticsearch.*</c>, <c>search.meilisearch.*</c>).
/// </summary>
public static class SearchErrorCodes
{
    /// <summary>The named index does not exist (<c>NotFound</c>).</summary>
    public const string IndexNotFound = "search.index_not_found";

    /// <summary>The named document does not exist or is not visible to the caller's tenant (<c>NotFound</c>).</summary>
    public const string DocumentNotFound = "search.document_not_found";

    /// <summary>The search request violates a provider-independent invariant (<c>Validation</c>).</summary>
    public const string InvalidRequest = "search.invalid_request";

    /// <summary>The filter predicate is malformed (<c>Validation</c>).</summary>
    public const string InvalidFilter = "search.invalid_filter";

    /// <summary>The index definition violates a structural invariant (<c>Validation</c>).</summary>
    public const string InvalidIndexDefinition = "search.invalid_index_definition";

    /// <summary>The document id violates the platform-wide charset rule (<c>Validation</c>).</summary>
    public const string InvalidDocumentId = "search.invalid_document_id";

    /// <summary>A field is not declared searchable on the index (<c>Validation</c>).</summary>
    public const string FieldNotSearchable = "search.field_not_searchable";

    /// <summary>A field is not declared filterable on the index (<c>Validation</c>).</summary>
    public const string FieldNotFilterable = "search.field_not_filterable";

    /// <summary>A field is not declared sortable on the index (<c>Validation</c>).</summary>
    public const string FieldNotSortable = "search.field_not_sortable";

    /// <summary>A field is not declared facetable on the index (<c>Validation</c>).</summary>
    public const string FieldNotFacetable = "search.field_not_facetable";

    /// <summary><c>Page * PageSize</c> exceeds the provider's <c>MaxTotalHits</c> ceiling (<c>Validation</c>).</summary>
    public const string PaginationLimitExceeded = "search.pagination_limit_exceeded";

    /// <summary>The requested facet-value count exceeds the provider's cap (<c>Validation</c>).</summary>
    public const string FacetLimitExceeded = "search.facet_limit_exceeded";

    /// <summary><c>ToPagedList()</c> was called on a result whose total is not exact (<c>Validation</c>).</summary>
    public const string TotalHitsNotExact = "search.total_hits_not_exact";

    /// <summary>A capability the provider cannot honour completely (<c>Validation</c>).</summary>
    public const string NotSupported = "search.not_supported";

    /// <summary>The index already exists (<c>Conflict</c>).</summary>
    public const string IndexAlreadyExists = "search.index_already_exists";

    /// <summary>A field declaration conflicts with the live mapping (<c>Conflict</c>).</summary>
    public const string IndexDefinitionConflict = "search.index_definition_conflict";

    /// <summary>The staging-to-live cutover failed (<c>Conflict</c>).</summary>
    public const string CutoverFailed = "search.cutover_failed";

    /// <summary>The live index's schema fingerprint differs from the expected definition (<c>Conflict</c>).</summary>
    public const string SchemaFingerprintMismatch = "search.schema_fingerprint_mismatch";

    /// <summary>The engine rejected the kernel's own credentials, a deployment defect (<c>Unexpected</c>).</summary>
    public const string EngineUnauthorized = "search.engine_unauthorized";

    /// <summary>A tenanted index was called with <c>TenantScope.Global</c>, refused before any I/O (<c>Unauthorized</c>).</summary>
    public const string TenantScopeMissing = "search.tenant_scope_missing";

    /// <summary>The engine could not be reached or refused the call as overloaded (<c>Unavailable</c>).</summary>
    public const string Unavailable = "search.unavailable";

    /// <summary>The operation exceeded its allotted time (<c>Timeout</c>).</summary>
    public const string Timeout = "search.timeout";

    /// <summary>The engine rejected a write outright (<c>Unexpected</c>).</summary>
    public const string WriteRejected = "search.write_rejected";

    /// <summary>Waiting for a write to become searchable timed out (<c>Timeout</c>).</summary>
    public const string WriteTimeout = "search.write_timeout";

    /// <summary>A bulk operation reported per-item failures (<c>Unexpected</c>).</summary>
    public const string BulkPartiallyFailed = "search.bulk_partially_failed";

    /// <summary>Probing an index's readiness failed outright (<c>Unexpected</c>).</summary>
    public const string ProbeFailed = "search.probe_failed";

    /// <summary>The connected engine's version is outside the supported range (<c>Unexpected</c>).</summary>
    public const string EngineVersionNotSupported = "search.engine_version_not_supported";

    /// <summary>A recognised engine reply the kernel has no specific mapping for (<c>Unexpected</c>).</summary>
    public const string EngineFault = "search.engine_fault";
}
