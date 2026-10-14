using SharedKernel.Primitives.Errors;

namespace SharedKernel.Search.Meilisearch.Errors;

/// <summary>The Meilisearch-exclusive <see cref="Error"/> factory, for failure modes with no ElasticSearch analogue.</summary>
internal static class MeilisearchErrors
{
    /// <summary>
    /// A Meilisearch task reached the <c>failed</c> state — has no ElasticSearch analogue, since
    /// ElasticSearch has no task queue.
    /// </summary>
    public static Error IndexingTaskFailed(string providerToken, string engineErrorCode) =>
        Error.Unexpected(
            MeilisearchErrorCodes.IndexingTaskFailed,
            $"Meilisearch task '{providerToken}' failed with engine error code '{engineErrorCode}'.");

    /// <summary>Issuing a tenant search token failed.</summary>
    public static Error TenantTokenIssuanceFailed(string reason) =>
        Error.Unexpected(
            MeilisearchErrorCodes.TenantTokenIssuanceFailed,
            $"Issuing the tenant search token failed: {reason}");

    /// <summary>The requested tenant search token TTL exceeds <c>MeilisearchOptions.TenantTokenMaxTtlMinutes</c>.</summary>
    public static Error TenantTokenTtlOutOfRange(TimeSpan requested, int maxMinutes) =>
        Error.Validation(
            MeilisearchErrorCodes.TenantTokenTtlOutOfRange,
            $"The requested tenant token lifetime of {requested} is outside the allowed range, which is up to {maxMinutes} minutes.");
}
