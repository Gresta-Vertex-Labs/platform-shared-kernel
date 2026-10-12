using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Errors;

/// <summary>
/// The canonical <see cref="Error"/> factory for <c>10.Intelligence</c>. Provider implementations
/// return these — never an ad-hoc <see cref="Error"/> value constructed inline.
/// </summary>
/// <remarks>
/// <para>
/// Only these <see cref="SharedKernel.Primitives.Errors.Error"/> factories are used:
/// <see cref="Error.Unexpected"/>, <see cref="Error.Validation"/>, <see cref="Error.NotFound"/>,
/// <see cref="Error.Conflict"/>, <see cref="Error.Unauthorized"/>, <see cref="Error.Unavailable"/> (a provider
/// that is down or throttling) and <see cref="Error.Timeout"/> (an operation that ran out of time).
/// <see cref="Error.BusinessRule"/> is used zero times in this domain — nothing in a capability package is a
/// domain rule. <see cref="Error.None"/> is never returned by any member here.
/// </para>
/// <para>
/// <b>Message text contract.</b> <see cref="Error.Message"/> holds kernel-authored wording and bounded identifiers
/// only: provider, operation, collection and model names, the caller's own record id, numbers, an HTTP status or a
/// gRPC status name. It never holds an exception message, a gRPC status detail, a response body, an endpoint or
/// host, a filter value, a key or prompt text, because the message reaches ProblemDetails; the detail reaches the
/// log through the exception parameter instead. The <c>reason</c> parameters below are kernel-authored text and
/// must never be filled from provider or exception text.
/// </para>
/// </remarks>
public static class IntelligenceErrors
{
    private const string CollectionNotFoundCode = "intelligence.collection_not_found";
    private const string RecordNotFoundCode = "intelligence.record_not_found";
    private const string ModelNotFoundCode = "intelligence.model_not_found";
    private const string InvalidQueryCode = "intelligence.invalid_query";
    private const string InvalidFilterCode = "intelligence.invalid_filter";
    private const string InvalidCollectionDefinitionCode = "intelligence.invalid_collection_definition";
    private const string InvalidRecordIdCode = "intelligence.invalid_record_id";
    private const string FieldNotFilterableCode = "intelligence.field_not_filterable";
    private const string EmbeddingModelMismatchCode = "intelligence.embedding_model_mismatch";
    private const string DimensionMismatchCode = "intelligence.dimension_mismatch";
    private const string DistanceMetricMismatchCode = "intelligence.distance_metric_mismatch";
    private const string BatchSizeExceededCode = "intelligence.batch_size_exceeded";
    private const string FilterDepthExceededCode = "intelligence.filter_depth_exceeded";
    private const string ContextWindowExceededCode = "intelligence.context_window_exceeded";
    private const string UnsupportedCapabilityCode = "intelligence.unsupported_capability";
    private const string CollectionAlreadyExistsCode = "intelligence.collection_already_exists";
    private const string CollectionDefinitionConflictCode = "intelligence.collection_definition_conflict";
    private const string CutoverFailedCode = "intelligence.cutover_failed";
    private const string SchemaFingerprintMismatchCode = "intelligence.schema_fingerprint_mismatch";
    private const string UnauthorizedCode = "intelligence.unauthorized";
    private const string TenantScopeMissingCode = "intelligence.tenant_scope_missing";
    private const string UnreachableCode = "intelligence.unreachable";
    private const string TimeoutCode = "intelligence.timeout";
    private const string WriteRejectedCode = "intelligence.write_rejected";
    private const string WriteTimeoutCode = "intelligence.write_timeout";
    private const string BulkPartiallyFailedCode = "intelligence.bulk_partially_failed";
    private const string ProbeFailedCode = "intelligence.probe_failed";
    private const string EngineVersionUnsupportedCode = "intelligence.engine_version_unsupported";
    private const string EngineFaultCode = "intelligence.engine_fault";
    private const string RateLimitedCode = "intelligence.rate_limited";
    private const string CompletionFailedCode = "intelligence.completion_failed";

    /// <summary>The named vector collection does not exist.</summary>
    public static Error CollectionNotFound(string collectionName) =>
        Error.NotFound(CollectionNotFoundCode, $"Vector collection '{collectionName}' was not found.");

    /// <summary>The named record does not exist (or is not visible to the caller's tenant).</summary>
    public static Error RecordNotFound(string collectionName, string recordId) =>
        Error.NotFound(RecordNotFoundCode, $"Record '{recordId}' was not found in collection '{collectionName}'.");

    /// <summary>The named embedding/completion model does not exist or is not registered.</summary>
    public static Error ModelNotFound(string modelId) =>
        Error.NotFound(ModelNotFoundCode, $"Model '{modelId}' was not found.");

    /// <summary>The vector query violates a provider-independent structural invariant. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error InvalidQuery(string reason) =>
        Error.Validation(InvalidQueryCode, $"The vector query is invalid: {reason}");

    /// <summary>The filter predicate is malformed. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error InvalidFilter(string reason) =>
        Error.Validation(InvalidFilterCode, $"The vector filter is invalid: {reason}");

    /// <summary>The collection definition violates a structural invariant. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error InvalidCollectionDefinition(string reason) =>
        Error.Validation(InvalidCollectionDefinitionCode, $"The vector collection definition is invalid: {reason}");

    /// <summary>The record id violates a provider's charset or length constraint.</summary>
    public static Error InvalidRecordId(string recordId) =>
        Error.Validation(InvalidRecordIdCode, $"Record id '{recordId}' is not a legal identifier for this provider.");

    /// <summary>The field is not declared filterable on the collection.</summary>
    public static Error FieldNotFilterable(string collectionName, string field) =>
        Error.Validation(
            FieldNotFilterableCode,
            $"Field '{field}' on collection '{collectionName}' is not declared filterable.");

    /// <summary>
    /// The record/query's embedding model identity does not match the collection's declared
    /// <c>EmbeddingModelId</c> — the dangerous half of a model-identity mismatch, since no vector
    /// engine can detect it on its own.
    /// </summary>
    public static Error EmbeddingModelMismatch(string collectionName, string expected, string actual) =>
        Error.Validation(
            EmbeddingModelMismatchCode,
            $"Collection '{collectionName}' expects embedding model '{expected}' but received '{actual}'.");

    /// <summary>The record/query vector's dimension does not match the collection's declared dimension.</summary>
    public static Error DimensionMismatch(string collectionName, int expected, int actual) =>
        Error.Validation(
            DimensionMismatchCode,
            $"Collection '{collectionName}' expects dimension {expected} but received {actual}.");

    /// <summary>The declared distance metric does not match the collection's live declaration.</summary>
    public static Error DistanceMetricMismatch(string collectionName, string expected, string actual) =>
        Error.Validation(
            DistanceMetricMismatchCode,
            $"Collection '{collectionName}' expects distance metric '{expected}' but received '{actual}'.");

    /// <summary>The requested batch size exceeds the provider's declared ceiling.</summary>
    public static Error BatchSizeExceeded(int requested, int ceiling, string providerName) =>
        Error.Validation(
            BatchSizeExceededCode,
            $"Requested batch size {requested} exceeds provider '{providerName}''s ceiling of {ceiling}.");

    /// <summary>The filter tree's depth exceeds the provider's declared ceiling.</summary>
    public static Error FilterDepthExceeded(int requested, int ceiling) =>
        Error.Validation(
            FilterDepthExceededCode,
            $"Filter depth {requested} exceeds the provider's MaxFilterDepth ceiling of {ceiling}.");

    /// <summary>The estimated prompt size exceeds the provider's context-window ceiling.</summary>
    public static Error ContextWindowExceeded(int limit, int actual) =>
        Error.Validation(
            ContextWindowExceededCode,
            $"Estimated token count {actual} exceeds the provider's context-window limit of {limit}.");

    /// <summary>
    /// Reserved and currently unreachable by construction — the closed 8-node filter hierarchy has no
    /// untranslatable node today. Exists so a future filter node produces a loud failure on the
    /// lagging adapter rather than a dropped predicate.
    /// </summary>
    public static Error UnsupportedCapability(string capability, string providerName) =>
        Error.Validation(
            UnsupportedCapabilityCode,
            $"Capability '{capability}' is not supported by provider '{providerName}'.");

    /// <summary>The collection already exists (its creation was not idempotent for the caller's intent).</summary>
    public static Error CollectionAlreadyExists(string collectionName) =>
        Error.Conflict(CollectionAlreadyExistsCode, $"Vector collection '{collectionName}' already exists.");

    /// <summary>
    /// The requested collection definition conflicts with the live collection.
    /// <c>EnsureCollectionAsync</c> is additive-only and never rewrites an incompatible definition —
    /// the remedy is a staging collection, a bulk upsert, then <c>CutoverAsync</c>.
    /// </summary>
    public static Error CollectionDefinitionConflict(string collectionName, string field) =>
        Error.Conflict(
            CollectionDefinitionConflictCode,
            $"Field '{field}' on collection '{collectionName}' conflicts with the live definition. " +
            "EnsureCollectionAsync is additive-only; use a staging collection, bulk-upsert, then CutoverAsync to " +
            "change an incompatible field.");

    /// <summary>The staging-to-live cutover failed. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error CutoverFailed(string stagingCollectionName, string liveCollectionName, string reason) =>
        Error.Conflict(
            CutoverFailedCode,
            $"Cutover from staging collection '{stagingCollectionName}' to live collection " +
            $"'{liveCollectionName}' failed: {reason}");

    /// <summary>The live collection's recorded schema fingerprint does not match the expected definition.</summary>
    public static Error SchemaFingerprintMismatch(string collectionName, string expected, string actual) =>
        Error.Conflict(
            SchemaFingerprintMismatchCode,
            $"Collection '{collectionName}' schema fingerprint mismatch: expected '{expected}', found '{actual}'.");

    /// <summary>The caller is not authorized to perform the given operation on the collection.</summary>
    public static Error Unauthorized(string collectionName, string operation) =>
        Error.Unauthorized(
            UnauthorizedCode,
            $"Not authorized to perform '{operation}' on collection '{collectionName}'.");

    /// <summary>
    /// The collection declares a tenant field but the caller supplied <c>TenantScope.Global</c> — an
    /// isolation failure, fail-closed before any I/O.
    /// </summary>
    public static Error TenantScopeMissing(string collectionName) =>
        Error.Unauthorized(
            TenantScopeMissingCode,
            $"Collection '{collectionName}' declares a TenantField; TenantScope.Global is not permitted.");

    /// <summary>
    /// The vector or completion provider could not be reached (connection refused, DNS or TLS failure, gRPC
    /// <c>Unavailable</c>, HTTP 502, 503 or 504). A dependency outage is <see cref="ErrorType.Unavailable"/>. The
    /// message never names an endpoint or host.
    /// </summary>
    public static Error Unreachable(string providerName) =>
        Error.Unavailable(UnreachableCode, $"Provider '{providerName}' is unreachable.");

    /// <summary>
    /// The operation ran out of time before the provider answered (gRPC <c>DeadlineExceeded</c>, HTTP 408, or a
    /// client timeout while the caller's token was still live). Its outcome may be unknown; a timeout is
    /// <see cref="ErrorType.Timeout"/>.
    /// </summary>
    public static Error Timeout(string operation) =>
        Error.Timeout(TimeoutCode, $"Operation '{operation}' timed out.");

    /// <summary>The provider rejected the write outright. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error WriteRejected(string collectionName, string reason) =>
        Error.Unexpected(WriteRejectedCode, $"Write to collection '{collectionName}' was rejected: {reason}");

    /// <summary>
    /// Waiting for a write to become queryable timed out. The write may still land — this is distinct
    /// from <see cref="WriteRejected"/>, and callers must not retry blindly.
    /// </summary>
    public static Error WriteTimeout(string collectionName, TimeSpan elapsed) =>
        Error.Timeout(
            WriteTimeoutCode,
            $"Waiting for a write on collection '{collectionName}' to become queryable timed out after " +
            $"{elapsed}. The write may still land — do not retry blindly.");

    /// <summary>
    /// A canonical error for consumers/rebuild orchestrators that treat any bulk item failure as
    /// fatal. Not returned by <c>UpsertManyAsync</c>/<c>DeleteManyAsync</c> themselves — those report
    /// partial failure through <c>VectorBulkReceipt</c> on a success
    /// <see cref="SharedKernel.Primitives.Results.Result"/>.
    /// </summary>
    public static Error BulkPartiallyFailed(int failedCount, int totalCount) =>
        Error.Unexpected(
            BulkPartiallyFailedCode,
            $"{failedCount} of {totalCount} records failed in this bulk operation.");

    /// <summary>Probing the collection's readiness failed outright (as distinct from a degraded-but-answered probe). <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error ProbeFailed(string collectionName, string reason) =>
        Error.Unexpected(ProbeFailedCode, $"Probing collection '{collectionName}' failed: {reason}");

    /// <summary>The connected engine's version is outside the supported range.</summary>
    public static Error EngineVersionUnsupported(string actual, string supportedRange) =>
        Error.Unexpected(
            EngineVersionUnsupportedCode,
            $"Engine version '{actual}' is not supported; expected {supportedRange}.");

    /// <summary>
    /// The mapping for a recognised provider fault that has no more specific code. A rise in this code is the
    /// signal that a translator has drifted from a provider's current API surface.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="operation">The operation that faulted.</param>
    /// <param name="engineErrorCode">
    /// A bounded token naming the engine's own classification (a gRPC status name such as <c>Internal</c> or an HTTP
    /// status number), or <see langword="null"/> when there is none. Never exception or response text.
    /// </param>
    public static Error EngineFault(string providerName, string operation, string? engineErrorCode = null) =>
        Error.Unexpected(
            EngineFaultCode,
            engineErrorCode is null
                ? $"Provider '{providerName}' operation '{operation}' faulted."
                : $"Provider '{providerName}' operation '{operation}' faulted with engine error '{engineErrorCode}'.");

    /// <summary>
    /// The provider rate-limited the request (gRPC <c>ResourceExhausted</c>, HTTP 429). There is no rate-limit
    /// <see cref="ErrorType"/>; a throttling dependency is <see cref="ErrorType.Unavailable"/>.
    /// </summary>
    public static Error RateLimited(string providerName, TimeSpan? retryAfter) =>
        Error.Unavailable(
            RateLimitedCode,
            retryAfter is { } delay
                ? $"Provider '{providerName}' rate-limited the request; retry after {delay}."
                : $"Provider '{providerName}' rate-limited the request.");

    /// <summary>
    /// The completion call failed for a recognised reason not otherwise classified.
    /// <paramref name="reason"/> is kernel-authored text (for example an HTTP status), never provider or exception text.
    /// </summary>
    public static Error CompletionFailed(string providerName, string reason) =>
        Error.Unexpected(CompletionFailedCode, $"Provider '{providerName}' completion failed: {reason}");
}
