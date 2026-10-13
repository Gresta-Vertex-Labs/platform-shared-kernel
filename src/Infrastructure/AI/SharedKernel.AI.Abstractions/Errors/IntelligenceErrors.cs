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
/// log through the exception parameter instead. The <c>reason</c> parameters below are kernel-authored text,
/// written as a sentence that ends with a period, and must never be filled from provider or exception text.
/// </para>
/// <para>
/// The codes are the constants of <see cref="IntelligenceErrorCodes"/>. This factory is public (unlike the internal
/// factories of single-assembly capabilities) because both providers and the in-memory double live in separate
/// assemblies and build the same vocabulary.
/// </para>
/// </remarks>
public static class IntelligenceErrors
{
    /// <summary>The named vector collection does not exist.</summary>
    public static Error CollectionNotFound(string collectionName) =>
        Error.NotFound(IntelligenceErrorCodes.CollectionNotFound, $"Vector collection '{collectionName}' was not found.");

    /// <summary>The named record does not exist (or is not visible to the caller's tenant).</summary>
    public static Error RecordNotFound(string collectionName, string recordId) =>
        Error.NotFound(IntelligenceErrorCodes.RecordNotFound, $"Record '{recordId}' was not found in collection '{collectionName}'.");

    /// <summary>The named embedding/completion model does not exist or is not registered.</summary>
    public static Error ModelNotFound(string modelId) =>
        Error.NotFound(IntelligenceErrorCodes.ModelNotFound, $"Model '{modelId}' was not found.");

    /// <summary>The vector query violates a provider-independent structural invariant. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error InvalidQuery(string reason) =>
        Error.Validation(IntelligenceErrorCodes.InvalidQuery, $"The vector query is invalid: {reason}");

    /// <summary>The filter predicate is malformed. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error InvalidFilter(string reason) =>
        Error.Validation(IntelligenceErrorCodes.InvalidFilter, $"The vector filter is invalid: {reason}");

    /// <summary>The collection definition violates a structural invariant. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error InvalidCollectionDefinition(string reason) =>
        Error.Validation(IntelligenceErrorCodes.InvalidCollectionDefinition, $"The vector collection definition is invalid: {reason}");

    /// <summary>The record id violates a provider's charset or length constraint.</summary>
    public static Error InvalidRecordId(string recordId) =>
        Error.Validation(IntelligenceErrorCodes.InvalidRecordId, $"Record id '{recordId}' is not a legal identifier for this provider.");

    /// <summary>The field is not declared filterable on the collection.</summary>
    public static Error FieldNotFilterable(string collectionName, string field) =>
        Error.Validation(
            IntelligenceErrorCodes.FieldNotFilterable,
            $"Field '{field}' on collection '{collectionName}' is not declared filterable.");

    /// <summary>
    /// The record/query's embedding model identity does not match the collection's declared
    /// <c>EmbeddingModelId</c> — the dangerous half of a model-identity mismatch, since no vector
    /// engine can detect it on its own.
    /// </summary>
    public static Error EmbeddingModelMismatch(string collectionName, string expected, string actual) =>
        Error.Validation(
            IntelligenceErrorCodes.EmbeddingModelMismatch,
            $"Collection '{collectionName}' expects embedding model '{expected}' but received '{actual}'.");

    /// <summary>The record/query vector's dimension does not match the collection's declared dimension.</summary>
    public static Error DimensionMismatch(string collectionName, int expected, int actual) =>
        Error.Validation(
            IntelligenceErrorCodes.DimensionMismatch,
            $"Collection '{collectionName}' expects dimension {expected} but received {actual}.");

    /// <summary>The declared distance metric does not match the collection's live declaration.</summary>
    public static Error DistanceMetricMismatch(string collectionName, string expected, string actual) =>
        Error.Validation(
            IntelligenceErrorCodes.DistanceMetricMismatch,
            $"Collection '{collectionName}' expects distance metric '{expected}' but received '{actual}'.");

    /// <summary>The requested batch size exceeds the provider's declared ceiling.</summary>
    public static Error BatchSizeExceeded(int requested, int ceiling, string providerName) =>
        Error.Validation(
            IntelligenceErrorCodes.BatchSizeExceeded,
            $"Requested batch size {requested} exceeds the ceiling of {ceiling} for provider '{providerName}'.");

    /// <summary>The filter tree's depth exceeds the provider's declared ceiling.</summary>
    public static Error FilterDepthExceeded(int requested, int ceiling) =>
        Error.Validation(
            IntelligenceErrorCodes.FilterDepthExceeded,
            $"Filter depth {requested} must not exceed the ceiling of {ceiling}.");

    /// <summary>The estimated prompt size exceeds the provider's context-window ceiling.</summary>
    public static Error ContextWindowExceeded(int limit, int actual) =>
        Error.Validation(
            IntelligenceErrorCodes.ContextWindowExceeded,
            $"Estimated token count {actual} must not exceed the context window of {limit}.");

    /// <summary>
    /// Reserved and currently unreachable by construction — the closed 8-node filter hierarchy has no
    /// untranslatable node today. Exists so a future filter node produces a loud failure on the
    /// lagging adapter rather than a dropped predicate.
    /// </summary>
    public static Error NotSupported(string capability, string providerName) =>
        Error.Validation(
            IntelligenceErrorCodes.NotSupported,
            $"Capability '{capability}' is not supported by provider '{providerName}'.");

    /// <summary>The collection already exists (its creation was not idempotent for the caller's intent).</summary>
    public static Error CollectionAlreadyExists(string collectionName) =>
        Error.Conflict(IntelligenceErrorCodes.CollectionAlreadyExists, $"Vector collection '{collectionName}' already exists.");

    /// <summary>
    /// The requested collection definition conflicts with the live collection.
    /// <c>EnsureCollectionAsync</c> is additive-only and never rewrites an incompatible definition —
    /// the remedy is a staging collection, a bulk upsert, then <c>CutoverAsync</c>.
    /// </summary>
    public static Error CollectionDefinitionConflict(string collectionName, string field) =>
        Error.Conflict(
            IntelligenceErrorCodes.CollectionDefinitionConflict,
            $"Field '{field}' on collection '{collectionName}' conflicts with the live definition. " +
            "Use a staging collection, a bulk upsert and CutoverAsync to change an incompatible field.");

    /// <summary>The staging-to-live cutover failed. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error CutoverFailed(string stagingCollectionName, string liveCollectionName, string reason) =>
        Error.Conflict(
            IntelligenceErrorCodes.CutoverFailed,
            $"Cutover from staging collection '{stagingCollectionName}' to live collection " +
            $"'{liveCollectionName}' failed. {reason}");

    /// <summary>The live collection's recorded schema fingerprint does not match the expected definition.</summary>
    public static Error SchemaFingerprintMismatch(string collectionName, string expected, string actual) =>
        Error.Conflict(
            IntelligenceErrorCodes.SchemaFingerprintMismatch,
            $"Collection '{collectionName}' schema fingerprint mismatch: expected '{expected}', found '{actual}'.");

    /// <summary>
    /// The provider rejected the kernel's own credentials (gRPC <c>PermissionDenied</c> or <c>Unauthenticated</c>,
    /// HTTP 401 or 403). This is a deployment defect, not the end user's authentication, so it is
    /// <see cref="ErrorType.Unexpected"/>: answering the end user 401 would send clients into re-authentication loops.
    /// </summary>
    public static Error EngineUnauthorized(string providerName, string operation) =>
        Error.Unexpected(
            IntelligenceErrorCodes.EngineUnauthorized,
            $"Provider '{providerName}' rejected the credentials for operation '{operation}'.");

    /// <summary>
    /// The collection declares a tenant field but the caller supplied <c>TenantScope.Global</c> — an
    /// isolation failure, fail-closed before any I/O.
    /// </summary>
    public static Error TenantScopeMissing(string collectionName) =>
        Error.Unauthorized(
            IntelligenceErrorCodes.TenantScopeMissing,
            $"Collection '{collectionName}' declares a tenant field, so TenantScope.Global must not be used.");

    /// <summary>
    /// The vector or completion provider could not be reached (connection refused, DNS or TLS failure, gRPC
    /// <c>Unavailable</c>, HTTP 502, 503 or 504). A dependency outage is <see cref="ErrorType.Unavailable"/>. The
    /// message never names an endpoint or host.
    /// </summary>
    public static Error Unavailable(string providerName) =>
        Error.Unavailable(IntelligenceErrorCodes.Unavailable, $"Provider '{providerName}' is unavailable.");

    /// <summary>
    /// The operation ran out of time before the provider answered (gRPC <c>DeadlineExceeded</c>, HTTP 408, or a
    /// client timeout while the caller's token was still live). Its outcome may be unknown; a timeout is
    /// <see cref="ErrorType.Timeout"/>.
    /// </summary>
    public static Error Timeout(string operation) =>
        Error.Timeout(IntelligenceErrorCodes.Timeout, $"Operation '{operation}' timed out.");

    /// <summary>The provider rejected the write outright. <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error WriteRejected(string collectionName, string reason) =>
        Error.Unexpected(IntelligenceErrorCodes.WriteRejected, $"Write to collection '{collectionName}' was rejected. {reason}");

    /// <summary>
    /// Waiting for a write to become queryable timed out. The write may still land — this is distinct
    /// from <see cref="WriteRejected"/>, and callers must not retry blindly.
    /// </summary>
    public static Error WriteTimeout(string collectionName, TimeSpan elapsed) =>
        Error.Timeout(
            IntelligenceErrorCodes.WriteTimeout,
            $"Waiting for a write on collection '{collectionName}' to become queryable timed out after " +
            $"{elapsed}. The write may still land, so do not retry blindly.");

    /// <summary>
    /// A canonical error for consumers/rebuild orchestrators that treat any bulk item failure as
    /// fatal. Not returned by <c>UpsertManyAsync</c>/<c>DeleteManyAsync</c> themselves — those report
    /// partial failure through <c>VectorBulkReceipt</c> on a success
    /// <see cref="SharedKernel.Primitives.Results.Result"/>.
    /// </summary>
    public static Error BulkPartiallyFailed(int failedCount, int totalCount) =>
        Error.Unexpected(
            IntelligenceErrorCodes.BulkPartiallyFailed,
            $"{failedCount} of {totalCount} records failed in this bulk operation.");

    /// <summary>Probing the collection's readiness failed outright (as distinct from a degraded-but-answered probe). <paramref name="reason"/> is kernel-authored text, never provider or exception text.</summary>
    public static Error ProbeFailed(string collectionName, string reason) =>
        Error.Unexpected(IntelligenceErrorCodes.ProbeFailed, $"Probing collection '{collectionName}' failed. {reason}");

    /// <summary>The connected engine's version is outside the supported range.</summary>
    public static Error EngineVersionNotSupported(string actual, string supportedRange) =>
        Error.Unexpected(
            IntelligenceErrorCodes.EngineVersionNotSupported,
            $"Engine version '{actual}' is not supported, and the supported range is {supportedRange}.");

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
            IntelligenceErrorCodes.EngineFault,
            engineErrorCode is null
                ? $"Provider '{providerName}' operation '{operation}' faulted."
                : $"Provider '{providerName}' operation '{operation}' faulted with engine error '{engineErrorCode}'.");

    /// <summary>
    /// The provider rate-limited the request (gRPC <c>ResourceExhausted</c>, HTTP 429). There is no rate-limit
    /// <see cref="ErrorType"/>; a throttling dependency is <see cref="ErrorType.Unavailable"/>.
    /// </summary>
    public static Error RateLimited(string providerName, TimeSpan? retryAfter) =>
        Error.Unavailable(
            IntelligenceErrorCodes.RateLimited,
            retryAfter is { } delay
                ? $"Provider '{providerName}' rate-limited the request; retry after {delay}."
                : $"Provider '{providerName}' rate-limited the request.");

    /// <summary>
    /// The completion call failed for a recognised reason not otherwise classified.
    /// <paramref name="reason"/> is kernel-authored text (for example an HTTP status), never provider or exception text.
    /// </summary>
    public static Error CompletionFailed(string providerName, string reason) =>
        Error.Unexpected(IntelligenceErrorCodes.CompletionFailed, $"Provider '{providerName}' completion failed. {reason}");
}
