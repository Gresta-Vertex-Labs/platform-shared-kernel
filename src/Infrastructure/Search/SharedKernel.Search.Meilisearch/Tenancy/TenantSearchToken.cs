namespace SharedKernel.Search.Meilisearch.Tenancy;

/// <summary>An engine-enforced, per-tenant Meilisearch search token.</summary>
/// <remarks>
/// Tokens are not tracked server-side and cannot be revoked before expiry. They scope search only —
/// they do not scope document writes, which remain the caller's <c>TenantScope</c> responsibility on
/// <c>ISearchIndex&lt;TDocument&gt;</c>.
/// </remarks>
public sealed record TenantSearchToken
{
    /// <summary>Gets the signed JWT value.</summary>
    public required string Value { get; init; }

    /// <summary>Gets the time this token expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Gets the index names this token is scoped to.</summary>
    public required IReadOnlyList<string> ScopedIndexes { get; init; }
}
