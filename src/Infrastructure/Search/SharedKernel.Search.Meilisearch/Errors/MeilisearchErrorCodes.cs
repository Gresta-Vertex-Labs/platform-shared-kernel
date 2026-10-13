namespace SharedKernel.Search.Meilisearch.Errors;

/// <summary>
/// The stable codes of the failures only the Meilisearch provider produces, <c>search.meilisearch.{snake_reason}</c>. The
/// neutral codes (<c>search.unavailable</c>, <c>search.timeout</c>, ...) are in <c>SearchErrorCodes</c> and identical on both
/// providers. A service that branches on a code below has taken a dependency on this engine.
/// </summary>
public static class MeilisearchErrorCodes
{
    /// <summary>A Meilisearch task reached the <c>failed</c> state (<c>Unexpected</c>).</summary>
    public const string IndexingTaskFailed = "search.meilisearch.indexing_task_failed";

    /// <summary>Signing or configuring a tenant search token failed (<c>Unexpected</c>).</summary>
    public const string TenantTokenIssuanceFailed = "search.meilisearch.tenant_token_issuance_failed";

    /// <summary>The requested tenant token lifetime is outside the configured range (<c>Validation</c>).</summary>
    public const string TenantTokenTtlOutOfRange = "search.meilisearch.tenant_token_ttl_out_of_range";
}
