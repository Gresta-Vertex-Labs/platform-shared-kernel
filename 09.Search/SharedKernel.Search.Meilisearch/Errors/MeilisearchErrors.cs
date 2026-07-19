using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.Meilisearch.Errors;

/// <summary>The Meilisearch-exclusive <see cref="Error"/> factory, for failure modes with no ElasticSearch analogue.</summary>
public static class MeilisearchErrors
{
    private const string IndexingTaskFailedCode = "search.meilisearch.indexing_task_failed";
    private const string TenantTokenIssuanceFailedCode = "search.meilisearch.tenant_token_issuance_failed";
    private const string TenantTokenTtlOutOfRangeCode = "search.meilisearch.tenant_token_ttl_out_of_range";

    /// <summary>
    /// A Meilisearch task reached the <c>failed</c> state — has no ElasticSearch analogue, since
    /// ElasticSearch has no task queue.
    /// </summary>
    public static Error IndexingTaskFailed(string providerToken, string engineErrorCode) =>
        Error.Unexpected(
            IndexingTaskFailedCode,
            $"Meilisearch task '{providerToken}' failed with engine error code '{engineErrorCode}'.");

    /// <summary>Issuing a tenant search token failed.</summary>
    public static Error TenantTokenIssuanceFailed(string reason) =>
        Error.Unexpected(
            TenantTokenIssuanceFailedCode,
            $"Failed to issue a Meilisearch tenant search token: {reason}");

    /// <summary>The requested tenant search token TTL exceeds <c>MeilisearchOptions.TenantTokenMaxTtlMinutes</c>.</summary>
    public static Error TenantTokenTtlOutOfRange(TimeSpan requested, int maxMinutes) =>
        Error.Validation(
            TenantTokenTtlOutOfRangeCode,
            $"Requested tenant token TTL ({requested}) exceeds the configured maximum of {maxMinutes} minute(s).");
}
