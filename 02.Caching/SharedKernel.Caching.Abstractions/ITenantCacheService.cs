namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A tenant-scoped wrapper over <see cref="ICacheService"/> that requires an explicit,
/// non-defaulted tenant identifier on every read, write, and invalidation method — so
/// tenant-scoped key construction is structurally guaranteed rather than merely
/// possible-to-get-right by convention.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Recommended default entry point for a multi-tenant consumer.</strong>
/// <see cref="ITenantCacheKeyProvider"/> remains available and fully supported for callers
/// that need raw tenant-scoped key construction only (e.g., building a key to pass to some
/// other API) — neither type is deprecated in favor of the other. A service with genuinely
/// global (non-tenant-scoped) cached data should continue to inject <see cref="ICacheService"/>
/// directly; <see cref="ITenantCacheService"/> is a peer, not a universal replacement.
/// </para>
/// <para>
/// Every method takes <c>(entity, id)</c> — matching <see cref="ITenantCacheKeyProvider.BuildTenantKey"/>'s
/// own shape — never a pre-built key string, so a caller cannot bypass tenant-key construction
/// by handing in an already-built key.
/// </para>
/// <para>
/// <strong>Tag tenant-scoping.</strong> FusionCache tags (<see cref="CachePolicy.WithTags"/>)
/// live in a global namespace that <see cref="ITenantCacheKeyProvider"/> never touches. A
/// conforming implementation of <see cref="SetAsync{T}"/> rewrites every tag on the supplied
/// <see cref="CachePolicy"/> to a tenant-scoped form before delegating, and
/// <see cref="RemoveByTagAsync"/> applies the identical rewrite before invalidating — otherwise
/// two tenants sharing one tag name (e.g., both calling <c>.WithTags("orders")</c>) would
/// cross-invalidate each other's cache entries, a cross-tenant <em>invalidation</em> vector
/// strictly worse than a read leak.
/// </para>
/// <para>
/// <strong>Zero dependency on <c>12.Security</c> or <c>IHttpContextAccessor</c>.</strong>
/// <c>tenantId</c> is always the caller's explicit argument — never resolved from ambient
/// context — mirroring <see cref="ITenantCacheKeyProvider"/>'s existing rule verbatim.
/// </para>
/// </remarks>
public interface ITenantCacheService
{
    /// <summary>
    /// Attempts to retrieve the tenant-scoped cached value for <paramref name="entity"/> and
    /// <paramref name="id"/>, within the scope of <paramref name="tenantId"/>.
    /// Returns <see langword="null"/> when the key is not present in either L1 or L2.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="tenantId">
    /// The tenant identifier used to namespace the key. Must not be null or whitespace. Always
    /// supplied explicitly by the caller — never resolved from ambient context.
    /// </param>
    /// <param name="entity">The entity type or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached value, or <see langword="null"/> if not found.</returns>
    ValueTask<T?> GetAsync<T>(string tenantId, string entity, string id, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="value"/> in the cache under the tenant-scoped key derived from
    /// <paramref name="tenantId"/>, <paramref name="entity"/>, and <paramref name="id"/>, using
    /// the supplied <paramref name="policy"/>.
    /// </summary>
    /// <remarks>
    /// Any tags carried by <paramref name="policy"/> are rewritten to a tenant-scoped form before
    /// the entry is written, so tag-based invalidation cannot cross tenant boundaries.
    /// </remarks>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="tenantId">
    /// The tenant identifier used to namespace the key. Must not be null or whitespace. Always
    /// supplied explicitly by the caller — never resolved from ambient context.
    /// </param>
    /// <param name="entity">The entity type or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="policy">Cache policy controlling TTL, tags, and refresh behaviour.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetAsync<T>(
        string tenantId,
        string entity,
        string id,
        T value,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the tenant-scoped cached value for <paramref name="entity"/> and
    /// <paramref name="id"/> if present; otherwise invokes <paramref name="factory"/>, caches
    /// the result using <paramref name="policy"/>, and returns it.
    /// </summary>
    /// <remarks>
    /// This is the preferred method for all tenant-scoped cache reads — it inherits
    /// <see cref="ICacheService.GetOrSetAsync{T}"/>'s stampede-protection guarantee: the factory
    /// is called exactly once even under concurrent requests for the same tenant-scoped key.
    /// </remarks>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="tenantId">
    /// The tenant identifier used to namespace the key. Must not be null or whitespace. Always
    /// supplied explicitly by the caller — never resolved from ambient context.
    /// </param>
    /// <param name="entity">The entity type or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="factory">
    /// Async delegate invoked on cache miss. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="policy">Cache policy controlling TTL, tags, and refresh behaviour.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached or freshly-computed value.</returns>
    ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the tenant-scoped entry for <paramref name="entity"/> and <paramref name="id"/>
    /// from all cache layers. No-ops silently if the key does not exist.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant identifier used to namespace the key. Must not be null or whitespace. Always
    /// supplied explicitly by the caller — never resolved from ambient context.
    /// </param>
    /// <param name="entity">The entity type or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAsync(string tenantId, string entity, string id, CancellationToken ct = default);

    /// <summary>
    /// Removes all cache entries that carry the specified <paramref name="tag"/>, scoped to
    /// <paramref name="tenantId"/> only.
    /// </summary>
    /// <remarks>
    /// The tag is rewritten to its tenant-scoped form before invalidation, so this call can never
    /// evict another tenant's entries even when two tenants both use the same tag name.
    /// </remarks>
    /// <param name="tenantId">
    /// The tenant identifier scoping this invalidation. Must not be null or whitespace. Always
    /// supplied explicitly by the caller — never resolved from ambient context.
    /// </param>
    /// <param name="tag">The tag whose tenant-scoped entries should be evicted.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveByTagAsync(string tenantId, string tag, CancellationToken ct = default);
}
