using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.Abstractions.Errors;

/// <summary>
/// The canonical <see cref="Error"/> factory for <c>09.Search</c>. Provider implementations return
/// these — never an ad-hoc <see cref="Error"/> value constructed inline.
/// </summary>
/// <remarks>
/// Only real <see cref="SharedKernel.Primitives.Errors.Error"/> factories are used:
/// <see cref="Error.Unexpected"/>, <see cref="Error.Validation(string, string)"/>, <see cref="Error.NotFound"/>,
/// <see cref="Error.Conflict"/>, <see cref="Error.Unauthorized"/>, and — for an engine that is down or
/// too slow — <see cref="Error.Unavailable"/> (<see cref="Unreachable"/>) and <see cref="Error.Timeout"/>
/// (<see cref="Timeout"/>). <see cref="Error.BusinessRule"/> is used zero times in this domain —
/// nothing in a capability package is a domain rule. <see cref="Error.None"/> is never returned by any
/// member here.
/// </remarks>
public static class SearchErrors
{
    private const string IndexNotFoundCode = "search.index_not_found";
    private const string DocumentNotFoundCode = "search.document_not_found";
    private const string InvalidSearchRequestCode = "search.invalid_request";
    private const string InvalidFilterCode = "search.invalid_filter";
    private const string InvalidIndexDefinitionCode = "search.invalid_index_definition";
    private const string InvalidDocumentIdCode = "search.invalid_document_id";
    private const string FieldNotSearchableCode = "search.field_not_searchable";
    private const string FieldNotFilterableCode = "search.field_not_filterable";
    private const string FieldNotSortableCode = "search.field_not_sortable";
    private const string FieldNotFacetableCode = "search.field_not_facetable";
    private const string PaginationLimitExceededCode = "search.pagination_limit_exceeded";
    private const string FacetLimitExceededCode = "search.facet_limit_exceeded";
    private const string TotalHitsNotExactCode = "search.total_hits_not_exact";
    private const string UnsupportedCapabilityCode = "search.unsupported_capability";
    private const string IndexAlreadyExistsCode = "search.index_already_exists";
    private const string IndexDefinitionConflictCode = "search.index_definition_conflict";
    private const string CutoverFailedCode = "search.cutover_failed";
    private const string SchemaFingerprintMismatchCode = "search.schema_fingerprint_mismatch";
    private const string UnauthorizedCode = "search.unauthorized";
    private const string TenantScopeMissingCode = "search.tenant_scope_missing";
    private const string UnreachableCode = "search.unreachable";
    private const string TimeoutCode = "search.timeout";
    private const string WriteRejectedCode = "search.write_rejected";
    private const string WriteTimeoutCode = "search.write_timeout";
    private const string BulkPartiallyFailedCode = "search.bulk_partially_failed";
    private const string ProbeFailedCode = "search.probe_failed";
    private const string EngineVersionUnsupportedCode = "search.engine_version_unsupported";
    private const string EngineFaultCode = "search.engine_fault";

    /// <summary>The named index does not exist.</summary>
    public static Error IndexNotFound(string indexName) =>
        Error.NotFound(IndexNotFoundCode, $"Search index '{indexName}' was not found.");

    /// <summary>The named document does not exist (or is not visible to the caller's tenant).</summary>
    public static Error DocumentNotFound(string indexName, string documentId) =>
        Error.NotFound(DocumentNotFoundCode, $"Document '{documentId}' was not found in index '{indexName}'.");

    /// <summary>The search request violates a provider-independent structural invariant.</summary>
    public static Error InvalidSearchRequest(string reason) =>
        Error.Validation(InvalidSearchRequestCode, $"The search request is invalid: {reason}");

    /// <summary>The filter predicate is malformed.</summary>
    public static Error InvalidFilter(string reason) =>
        Error.Validation(InvalidFilterCode, $"The search filter is invalid: {reason}");

    /// <summary>The index definition violates a structural invariant.</summary>
    public static Error InvalidIndexDefinition(string reason) =>
        Error.Validation(InvalidIndexDefinitionCode, $"The index definition is invalid: {reason}");

    /// <summary>The document id violates the platform-wide charset rule (A-Z, a-z, 0-9, '-', '_').</summary>
    public static Error InvalidDocumentId(string documentId) =>
        Error.Validation(
            InvalidDocumentIdCode,
            $"Document id '{documentId}' must contain only A-Z, a-z, 0-9, '-', and '_'.");

    /// <summary>The field is not declared searchable on the index.</summary>
    public static Error FieldNotSearchable(string indexName, string field) =>
        Error.Validation(
            FieldNotSearchableCode,
            $"Field '{field}' on index '{indexName}' is not declared searchable.");

    /// <summary>The field is not declared filterable on the index.</summary>
    public static Error FieldNotFilterable(string indexName, string field) =>
        Error.Validation(
            FieldNotFilterableCode,
            $"Field '{field}' on index '{indexName}' is not declared filterable.");

    /// <summary>The field is not declared sortable on the index.</summary>
    public static Error FieldNotSortable(string indexName, string field) =>
        Error.Validation(
            FieldNotSortableCode,
            $"Field '{field}' on index '{indexName}' is not declared sortable.");

    /// <summary>The field is not declared facetable on the index.</summary>
    public static Error FieldNotFacetable(string indexName, string field) =>
        Error.Validation(
            FieldNotFacetableCode,
            $"Field '{field}' on index '{indexName}' is not declared facetable.");

    /// <summary>
    /// <c>Page * PageSize</c> exceeds the provider's <c>MaxTotalHits</c> ceiling.
    /// </summary>
    public static Error PaginationLimitExceeded(int page, int pageSize, int ceiling, string providerName) =>
        Error.Validation(
            PaginationLimitExceededCode,
            $"Page {page} with PageSize {pageSize} exceeds provider '{providerName}''s MaxTotalHits ceiling of " +
            $"{ceiling}. For deep pagination use ICursorSearch (ElasticSearch only), or " +
            "ISearchIndex.EnumerateAsync for an unordered corpus walk.");

    /// <summary>The requested facet-value count exceeds the provider's <c>MaxFacetValues</c> cap.</summary>
    public static Error FacetLimitExceeded(int requested, int ceiling) =>
        Error.Validation(
            FacetLimitExceededCode,
            $"Requested {requested} facet values exceeds the provider's MaxFacetValues ceiling of {ceiling}.");

    /// <summary><c>ToPagedList()</c> was called on a result whose <c>TotalHits</c> is not exact.</summary>
    public static Error TotalHitsNotExact() =>
        Error.Validation(
            TotalHitsNotExactCode,
            "TotalHits is not exact. Set SearchRequest.RequireExactTotalHits = true before calling ToPagedList().");

    /// <summary>
    /// Reserved and currently unreachable by construction — the closed 8-node filter hierarchy has no
    /// untranslatable node today. Exists so a future filter node produces a loud failure on the
    /// lagging adapter rather than a dropped predicate.
    /// </summary>
    public static Error UnsupportedCapability(string capability, string providerName) =>
        Error.Validation(
            UnsupportedCapabilityCode,
            $"Capability '{capability}' is not supported by provider '{providerName}'.");

    /// <summary>The index already exists (its creation was not idempotent for the caller's intent).</summary>
    public static Error IndexAlreadyExists(string indexName) =>
        Error.Conflict(IndexAlreadyExistsCode, $"Search index '{indexName}' already exists.");

    /// <summary>
    /// The requested field declaration conflicts with the live mapping.
    /// <c>EnsureIndexAsync</c> is additive-only and never rewrites an incompatible mapping — the
    /// remedy is a staging index, a bulk load, then <c>CutoverAsync</c>.
    /// </summary>
    public static Error IndexDefinitionConflict(string indexName, string field) =>
        Error.Conflict(
            IndexDefinitionConflictCode,
            $"Field '{field}' on index '{indexName}' conflicts with the live mapping. EnsureIndexAsync is " +
            "additive-only; use a staging index, bulk-load, then CutoverAsync to change an incompatible field.");

    /// <summary>The staging-to-live cutover failed.</summary>
    public static Error CutoverFailed(string stagingIndexName, string liveIndexName, string reason) =>
        Error.Conflict(
            CutoverFailedCode,
            $"Cutover from staging index '{stagingIndexName}' to live index '{liveIndexName}' failed: {reason}");

    /// <summary>The live index's recorded schema fingerprint does not match the expected definition.</summary>
    public static Error SchemaFingerprintMismatch(string indexName, string expected, string actual) =>
        Error.Conflict(
            SchemaFingerprintMismatchCode,
            $"Index '{indexName}' schema fingerprint mismatch: expected '{expected}', found '{actual}'.");

    /// <summary>The caller is not authorized to perform the given operation on the index.</summary>
    public static Error Unauthorized(string indexName, string operation) =>
        Error.Unauthorized(
            UnauthorizedCode,
            $"Not authorized to perform '{operation}' on index '{indexName}'.");

    /// <summary>
    /// The index declares a tenant field but the caller supplied <c>TenantScope.Global</c> — an
    /// isolation failure, fail-closed before any I/O.
    /// </summary>
    public static Error TenantScopeMissing(string indexName) =>
        Error.Unauthorized(
            TenantScopeMissingCode,
            $"Index '{indexName}' declares a TenantField; TenantScope.Global is not permitted.");

    /// <summary>
    /// The search provider could not be reached, or refused the call as overloaded — an
    /// <see cref="ErrorType.Unavailable"/> error, so the HTTP boundary answers 503 and the caller
    /// knows a retry may succeed.
    /// </summary>
    public static Error Unreachable(string providerName, string endpoint) =>
        Error.Unavailable(UnreachableCode, $"Search provider '{providerName}' at '{endpoint}' is unreachable.");

    /// <summary>
    /// The operation exceeded its allotted time — an <see cref="ErrorType.Timeout"/> error, so the
    /// HTTP boundary answers 504. A timed-out write may still have been applied.
    /// </summary>
    public static Error Timeout(string operation, TimeSpan elapsed) =>
        Error.Timeout(TimeoutCode, $"Operation '{operation}' timed out after {elapsed}.");

    /// <summary>The provider rejected the write outright.</summary>
    public static Error WriteRejected(string indexName, string reason) =>
        Error.Unexpected(WriteRejectedCode, $"Write to index '{indexName}' was rejected: {reason}");

    /// <summary>
    /// Waiting for a write to become searchable timed out (<see cref="ErrorType.Timeout"/>). The write may
    /// still land — this is distinct from <see cref="WriteRejected"/>, and callers must not retry blindly.
    /// </summary>
    public static Error WriteTimeout(string indexName, TimeSpan elapsed) =>
        Error.Timeout(
            WriteTimeoutCode,
            $"Waiting for a write on index '{indexName}' to become searchable timed out after {elapsed}. " +
            "The write may still land — do not retry blindly.");

    /// <summary>
    /// A canonical error for consumers/rebuild orchestrators that treat any bulk item failure as
    /// fatal. Not returned by <c>IndexManyAsync</c>/<c>DeleteManyAsync</c> themselves — those report
    /// partial failure through <see cref="SharedKernel.Search.Abstractions.Models.SearchBulkReceipt"/>
    /// on a success <see cref="SharedKernel.Primitives.Results.Result"/>.
    /// </summary>
    public static Error BulkPartiallyFailed(int failedCount, int totalCount) =>
        Error.Unexpected(
            BulkPartiallyFailedCode,
            $"{failedCount} of {totalCount} documents failed in this bulk operation.");

    /// <summary>Probing the index's readiness failed outright (as distinct from a degraded-but-answered probe).</summary>
    public static Error ProbeFailed(string indexName, string reason) =>
        Error.Unexpected(ProbeFailedCode, $"Probing index '{indexName}' failed: {reason}");

    /// <summary>The connected engine's version is outside the supported range.</summary>
    public static Error EngineVersionUnsupported(string actual, string supportedRange) =>
        Error.Unexpected(
            EngineVersionUnsupportedCode,
            $"Search engine version '{actual}' is not supported; expected {supportedRange}.");

    /// <summary>
    /// The last-resort mapping for an unclassifiable engine fault. A rise in this code is the signal
    /// that a translator has drifted from the engine's current API surface.
    /// </summary>
    public static Error EngineFault(string providerName, string operation, string detail) =>
        Error.Unexpected(
            EngineFaultCode,
            $"Provider '{providerName}' operation '{operation}' faulted: {detail}");
}
