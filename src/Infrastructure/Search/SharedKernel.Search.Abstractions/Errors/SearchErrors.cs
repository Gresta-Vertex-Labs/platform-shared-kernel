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
/// too slow — <see cref="Error.Unavailable"/> (<see cref="Unavailable"/>) and <see cref="Error.Timeout"/>
/// (<see cref="Timeout"/>). <see cref="Error.BusinessRule"/> is used zero times in this domain —
/// nothing in a capability package is a domain rule. <see cref="Error.None"/> is never returned by any
/// member here.
/// </remarks>
public static class SearchErrors
{

    /// <summary>The named index does not exist.</summary>
    public static Error IndexNotFound(string indexName) =>
        Error.NotFound(SearchErrorCodes.IndexNotFound, $"Search index '{indexName}' was not found.");

    /// <summary>The named document does not exist (or is not visible to the caller's tenant).</summary>
    public static Error DocumentNotFound(string indexName, string documentId) =>
        Error.NotFound(SearchErrorCodes.DocumentNotFound, $"Document '{documentId}' was not found in index '{indexName}'.");

    /// <summary>The search request violates a provider-independent structural invariant.</summary>
    /// <param name="reason">Kernel-authored text, never provider, engine or exception text.</param>
    public static Error InvalidSearchRequest(string reason) =>
        Error.Validation(SearchErrorCodes.InvalidRequest, $"The search request is invalid: {reason}");

    /// <summary>The filter predicate is malformed.</summary>
    /// <param name="reason">Kernel-authored text, never provider, engine or exception text, and never a filter value.</param>
    public static Error InvalidFilter(string reason) =>
        Error.Validation(SearchErrorCodes.InvalidFilter, $"The search filter is invalid: {reason}");

    /// <summary>The index definition violates a structural invariant.</summary>
    /// <param name="reason">Kernel-authored text, never provider, engine or exception text.</param>
    public static Error InvalidIndexDefinition(string reason) =>
        Error.Validation(SearchErrorCodes.InvalidIndexDefinition, $"The index definition is invalid: {reason}");

    /// <summary>The document id violates the platform-wide charset rule (A-Z, a-z, 0-9, '-', '_').</summary>
    public static Error InvalidDocumentId(string documentId) =>
        Error.Validation(
            SearchErrorCodes.InvalidDocumentId,
            $"Document id '{documentId}' must contain only A-Z, a-z, 0-9, '-', and '_'.");

    /// <summary>The field is not declared searchable on the index.</summary>
    public static Error FieldNotSearchable(string indexName, string field) =>
        Error.Validation(
            SearchErrorCodes.FieldNotSearchable,
            $"Field '{field}' on index '{indexName}' is not declared searchable.");

    /// <summary>The field is not declared filterable on the index.</summary>
    public static Error FieldNotFilterable(string indexName, string field) =>
        Error.Validation(
            SearchErrorCodes.FieldNotFilterable,
            $"Field '{field}' on index '{indexName}' is not declared filterable.");

    /// <summary>The field is not declared sortable on the index.</summary>
    public static Error FieldNotSortable(string indexName, string field) =>
        Error.Validation(
            SearchErrorCodes.FieldNotSortable,
            $"Field '{field}' on index '{indexName}' is not declared sortable.");

    /// <summary>The field is not declared facetable on the index.</summary>
    public static Error FieldNotFacetable(string indexName, string field) =>
        Error.Validation(
            SearchErrorCodes.FieldNotFacetable,
            $"Field '{field}' on index '{indexName}' is not declared facetable.");

    /// <summary>
    /// <c>Page * PageSize</c> exceeds the provider's <c>MaxTotalHits</c> ceiling.
    /// </summary>
    public static Error PaginationLimitExceeded(int page, int pageSize, int ceiling, string providerName) =>
        Error.Validation(
            SearchErrorCodes.PaginationLimitExceeded,
            $"Page {page} with PageSize {pageSize} exceeds the MaxTotalHits ceiling of {ceiling} of provider '{providerName}'. " +
            "Use ICursorSearch (ElasticSearch only) for deep pagination, or ISearchIndex.EnumerateAsync for an unordered corpus walk.");

    /// <summary>The requested facet-value count exceeds the provider's <c>MaxFacetValues</c> cap.</summary>
    public static Error FacetLimitExceeded(int requested, int ceiling) =>
        Error.Validation(
            SearchErrorCodes.FacetLimitExceeded,
            $"Requested {requested} facet values, which exceeds the MaxFacetValues ceiling of {ceiling}.");

    /// <summary><c>ToPagedList()</c> was called on a result whose <c>TotalHits</c> is not exact.</summary>
    public static Error TotalHitsNotExact() =>
        Error.Validation(
            SearchErrorCodes.TotalHitsNotExact,
            "TotalHits is not exact. Set SearchRequest.RequireExactTotalHits = true before calling ToPagedList().");

    /// <summary>
    /// Reserved and currently unreachable by construction: the closed 8-node filter hierarchy has no
    /// untranslatable node today. Exists so a future filter node produces a loud failure on the
    /// lagging adapter rather than a dropped predicate.
    /// </summary>
    public static Error NotSupported(string capability, string providerName) =>
        Error.Validation(
            SearchErrorCodes.NotSupported,
            $"Capability '{capability}' is not supported by provider '{providerName}'.");

    /// <summary>The index already exists (its creation was not idempotent for the caller's intent).</summary>
    public static Error IndexAlreadyExists(string indexName) =>
        Error.Conflict(SearchErrorCodes.IndexAlreadyExists, $"Search index '{indexName}' already exists.");

    /// <summary>
    /// The requested field declaration conflicts with the live mapping.
    /// <c>EnsureIndexAsync</c> is additive-only and never rewrites an incompatible mapping — the
    /// remedy is a staging index, a bulk load, then <c>CutoverAsync</c>.
    /// </summary>
    public static Error IndexDefinitionConflict(string indexName, string field) =>
        Error.Conflict(
            SearchErrorCodes.IndexDefinitionConflict,
            $"Field '{field}' on index '{indexName}' conflicts with the live mapping. EnsureIndexAsync is " +
            "additive-only; use a staging index, bulk-load, then CutoverAsync to change an incompatible field.");

    /// <summary>The staging-to-live cutover failed.</summary>
    /// <param name="stagingIndexName">The staging index name.</param>
    /// <param name="liveIndexName">The live index name.</param>
    /// <param name="reason">Kernel-authored text (a step name and an optional bounded code), never provider, engine or exception text.</param>
    public static Error CutoverFailed(string stagingIndexName, string liveIndexName, string reason) =>
        Error.Conflict(
            SearchErrorCodes.CutoverFailed,
            $"Cutover from staging index '{stagingIndexName}' to live index '{liveIndexName}' failed: {reason}");

    /// <summary>The live index's recorded schema fingerprint does not match the expected definition.</summary>
    public static Error SchemaFingerprintMismatch(string indexName, string expected, string actual) =>
        Error.Conflict(
            SearchErrorCodes.SchemaFingerprintMismatch,
            $"Index '{indexName}' schema fingerprint mismatch: expected '{expected}', found '{actual}'.");

    /// <summary>The engine rejected the kernel's own credentials for the operation, a deployment defect and not the end user's authentication (<see cref="ErrorType.Unexpected"/>).</summary>
    public static Error EngineUnauthorized(string indexName, string operation) =>
        Error.Unexpected(
            SearchErrorCodes.EngineUnauthorized,
            $"The engine rejected the credentials for operation '{operation}' on index '{indexName}'.");

    /// <summary>
    /// The index declares a tenant field but the caller supplied <c>TenantScope.Global</c> — an
    /// isolation failure, fail-closed before any I/O.
    /// </summary>
    public static Error TenantScopeMissing(string indexName) =>
        Error.Unauthorized(
            SearchErrorCodes.TenantScopeMissing,
            $"Index '{indexName}' declares a TenantField; TenantScope.Global is not permitted.");

    /// <summary>
    /// The search provider could not be reached, or refused the call as overloaded — an
    /// <see cref="ErrorType.Unavailable"/> error, so the HTTP boundary answers 503 and the caller
    /// knows a retry may succeed.
    /// </summary>
    public static Error Unavailable(string providerName) =>
        Error.Unavailable(SearchErrorCodes.Unavailable, $"Search provider '{providerName}' is unavailable.");

    /// <summary>
    /// The operation exceeded its allotted time — an <see cref="ErrorType.Timeout"/> error, so the
    /// HTTP boundary answers 504. A timed-out write may still have been applied.
    /// </summary>
    public static Error Timeout(string operation) =>
        Error.Timeout(SearchErrorCodes.Timeout, $"Operation '{operation}' timed out.");

    /// <summary>The provider rejected the write outright.</summary>
    /// <param name="indexName">The index name.</param>
    /// <param name="reason">Kernel-authored text, never provider, engine or exception text.</param>
    public static Error WriteRejected(string indexName, string reason) =>
        Error.Unexpected(SearchErrorCodes.WriteRejected, $"Write to index '{indexName}' was rejected: {reason}");

    /// <summary>
    /// Waiting for a write to become searchable timed out (<see cref="ErrorType.Timeout"/>). The write may
    /// still land — this is distinct from <see cref="WriteRejected"/>, and callers must not retry blindly.
    /// </summary>
    public static Error WriteTimeout(string indexName, TimeSpan elapsed) =>
        Error.Timeout(
            SearchErrorCodes.WriteTimeout,
            $"Waiting for a write on index '{indexName}' to become searchable timed out after {elapsed}. " +
            "The write may still land, so do not retry blindly.");

    /// <summary>
    /// A canonical error for consumers/rebuild orchestrators that treat any bulk item failure as
    /// fatal. Not returned by <c>IndexManyAsync</c>/<c>DeleteManyAsync</c> themselves — those report
    /// partial failure through <see cref="SharedKernel.Search.Abstractions.Models.SearchBulkReceipt"/>
    /// on a success <see cref="SharedKernel.Primitives.Results.Result"/>.
    /// </summary>
    public static Error BulkPartiallyFailed(int failedCount, int totalCount) =>
        Error.Unexpected(
            SearchErrorCodes.BulkPartiallyFailed,
            $"{failedCount} of {totalCount} documents failed in this bulk operation.");

    /// <summary>Probing the index's readiness failed outright (as distinct from a degraded-but-answered probe).</summary>
    /// <param name="indexName">The index name.</param>
    /// <param name="reason">Kernel-authored text, never provider, engine or exception text.</param>
    public static Error ProbeFailed(string indexName, string reason) =>
        Error.Unexpected(SearchErrorCodes.ProbeFailed, $"Probing index '{indexName}' failed: {reason}");

    /// <summary>The connected engine's version is outside the supported range.</summary>
    public static Error EngineVersionNotSupported(string actual, string supportedRange) =>
        Error.Unexpected(
            SearchErrorCodes.EngineVersionNotSupported,
            $"Search engine version '{actual}' is not supported, expected '{supportedRange}'.");

    /// <summary>
    /// A recognised engine reply the kernel has no specific mapping for. A rise in this code is the signal
    /// that a translator has drifted from the engine's current API surface. An unrecognised
    /// <em>exception</em> is never mapped here: it rethrows.
    /// </summary>
    /// <param name="providerName">The provider name from <c>SearchWellKnown</c>.</param>
    /// <param name="operation">The operation name.</param>
    /// <param name="engineErrorCode">
    /// The engine's own bounded error code (matching <c>^[a-z0-9_.]{1,64}$</c>), or <see langword="null"/>
    /// when the engine gave none. Never engine free text, a response body or an exception message.
    /// </param>
    public static Error EngineFault(string providerName, string operation, string? engineErrorCode = null) =>
        Error.Unexpected(
            SearchErrorCodes.EngineFault,
            engineErrorCode is null
                ? $"Provider '{providerName}' operation '{operation}' faulted."
                : $"Provider '{providerName}' operation '{operation}' faulted with engine error code '{engineErrorCode}'.");
}
