namespace SharedKernel.AI.Abstractions.Errors;

/// <summary>
/// The stable, machine-readable codes of <c>10.Intelligence</c>, lowercase <c>intelligence.{snake_reason}</c>. They are
/// the contract a caller, a dashboard or an alert keys on; <see cref="IntelligenceErrors"/> builds the
/// <see cref="SharedKernel.Primitives.Errors.Error"/> values from them.
/// </summary>
/// <remarks>
/// Outage is <c>unavailable</c>, a missing capability is <c>not_supported</c>, and the same condition carries the same
/// code on every provider. The provider rejecting the kernel's own key is <see cref="EngineUnauthorized"/>, a
/// deployment defect that is not the end user's authentication.
/// </remarks>
public static class IntelligenceErrorCodes
{
    /// <summary>The named vector collection does not exist.</summary>
    public const string CollectionNotFound = "intelligence.collection_not_found";

    /// <summary>The named record does not exist or is not visible to the caller's tenant.</summary>
    public const string RecordNotFound = "intelligence.record_not_found";

    /// <summary>The named embedding or completion model does not exist or is not registered.</summary>
    public const string ModelNotFound = "intelligence.model_not_found";

    /// <summary>The vector query violates a provider-independent structural invariant.</summary>
    public const string InvalidQuery = "intelligence.invalid_query";

    /// <summary>The filter predicate is malformed.</summary>
    public const string InvalidFilter = "intelligence.invalid_filter";

    /// <summary>The collection definition violates a structural invariant.</summary>
    public const string InvalidCollectionDefinition = "intelligence.invalid_collection_definition";

    /// <summary>The record id violates a provider's charset or length constraint.</summary>
    public const string InvalidRecordId = "intelligence.invalid_record_id";

    /// <summary>The field is not declared filterable on the collection.</summary>
    public const string FieldNotFilterable = "intelligence.field_not_filterable";

    /// <summary>The embedding model identity does not match the collection's declaration.</summary>
    public const string EmbeddingModelMismatch = "intelligence.embedding_model_mismatch";

    /// <summary>The vector dimension does not match the collection's declaration.</summary>
    public const string DimensionMismatch = "intelligence.dimension_mismatch";

    /// <summary>The distance metric does not match the collection's declaration.</summary>
    public const string DistanceMetricMismatch = "intelligence.distance_metric_mismatch";

    /// <summary>The requested batch size exceeds the provider's ceiling.</summary>
    public const string BatchSizeExceeded = "intelligence.batch_size_exceeded";

    /// <summary>The filter tree's depth exceeds the provider's ceiling.</summary>
    public const string FilterDepthExceeded = "intelligence.filter_depth_exceeded";

    /// <summary>The estimated prompt size exceeds the context window.</summary>
    public const string ContextWindowExceeded = "intelligence.context_window_exceeded";

    /// <summary>The provider does not support the requested capability.</summary>
    public const string NotSupported = "intelligence.not_supported";

    /// <summary>The collection already exists.</summary>
    public const string CollectionAlreadyExists = "intelligence.collection_already_exists";

    /// <summary>The requested definition conflicts with the live collection.</summary>
    public const string CollectionDefinitionConflict = "intelligence.collection_definition_conflict";

    /// <summary>The staging-to-live cutover failed.</summary>
    public const string CutoverFailed = "intelligence.cutover_failed";

    /// <summary>The live collection's schema fingerprint does not match the expected definition.</summary>
    public const string SchemaFingerprintMismatch = "intelligence.schema_fingerprint_mismatch";

    /// <summary>The provider rejected the kernel's own credentials, a deployment defect.</summary>
    public const string EngineUnauthorized = "intelligence.engine_unauthorized";

    /// <summary>A tenant-declaring collection was used with <c>TenantScope.Global</c>.</summary>
    public const string TenantScopeMissing = "intelligence.tenant_scope_missing";

    /// <summary>The provider could not be reached.</summary>
    public const string Unavailable = "intelligence.unavailable";

    /// <summary>The operation ran out of time before the provider answered.</summary>
    public const string Timeout = "intelligence.timeout";

    /// <summary>The provider rejected the write outright.</summary>
    public const string WriteRejected = "intelligence.write_rejected";

    /// <summary>Waiting for a write to become queryable timed out.</summary>
    public const string WriteTimeout = "intelligence.write_timeout";

    /// <summary>A bulk operation had failing items.</summary>
    public const string BulkPartiallyFailed = "intelligence.bulk_partially_failed";

    /// <summary>Probing the collection's readiness failed outright.</summary>
    public const string ProbeFailed = "intelligence.probe_failed";

    /// <summary>The connected engine's version is outside the supported range.</summary>
    public const string EngineVersionNotSupported = "intelligence.engine_version_not_supported";

    /// <summary>A recognised provider fault with no more specific code.</summary>
    public const string EngineFault = "intelligence.engine_fault";

    /// <summary>The provider rate-limited the request.</summary>
    public const string RateLimited = "intelligence.rate_limited";

    /// <summary>The completion call failed for a recognised reason not otherwise classified.</summary>
    public const string CompletionFailed = "intelligence.completion_failed";
}
