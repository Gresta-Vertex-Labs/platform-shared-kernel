using SharedKernel.Caching.Abstractions;

namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// A short, bounded-TTL caching decorator over any <see cref="ITenantCatalog"/>, stored in the
/// service's <see cref="ICacheService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A BOUNDED, SHORT DEFAULT TTL — NOT A PERF TRADEOFF.</b> Mirrors
/// <c>K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds</c>'s 30-second-default reasoning. AN
/// UNBOUNDED OR PURELY-TTL-BASED CACHE LETS A SUSPENDED TENANT KEEP OPERATING UNTIL THE TTL
/// EXPIRES — A CORRECTNESS BUG FOR A FINTECH-GRADE PLATFORM. Call <see cref="InvalidateTenantAsync"/>
/// the moment a tenant's status changes.
/// </para>
/// <para>
/// <b>Every instance, not just this one.</b> Entries live in <see cref="ICacheService"/>, so when the
/// service runs with a distributed cache and backplane (<c>AddRedisL2</c>),
/// <see cref="InvalidateTenantAsync"/> evicts the tenant on every instance. Without one, it evicts
/// only this instance and other instances fall back to the TTL.
/// </para>
/// <para>
/// <b>Fail-safe is off.</b> If the underlying catalog is unreachable after an entry expires, the
/// lookup fails rather than serving a stale descriptor, which could still say <c>Active</c> for a
/// suspended tenant. Eager refresh is off too.
/// </para>
/// <para>
/// <see cref="CatalogTenantStatusValidator"/> composes with this type with no code changes.
/// </para>
/// </remarks>
public sealed class CachedTenantCatalog : ITenantCatalog
{
    /// <summary>
    /// The default cache TTL: 30 seconds. Short and bounded deliberately — see the type-level remarks.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private const string ByIdEntity = "tenant-catalog";
    private const string ByResolutionKeyEntity = "tenant-catalog-resolution";

    private readonly ITenantCatalog _inner;
    private readonly ICacheService _cache;
    private readonly ICacheKeyProvider _keyProvider;
    private readonly CachePolicy _policy;

    /// <summary>
    /// Creates a new <see cref="CachedTenantCatalog"/> wrapping <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The <see cref="ITenantCatalog"/> to cache lookups from.</param>
    /// <param name="cache">The cache that stores descriptors.</param>
    /// <param name="keyProvider">Builds the service-prefixed cache keys.</param>
    /// <param name="ttl">
    /// The cache TTL. Defaults to <see cref="DefaultTtl"/> (30 seconds) when omitted. Must be positive.
    /// </param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is not positive.</exception>
    public CachedTenantCatalog(
        ITenantCatalog inner,
        ICacheService cache,
        ICacheKeyProvider keyProvider,
        TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(keyProvider);

        var resolvedTtl = ttl ?? DefaultTtl;
        if (resolvedTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), resolvedTtl, "The cache TTL must be positive.");
        }

        _inner = inner;
        _cache = cache;
        _keyProvider = keyProvider;
        _policy = CachePolicy.For(resolvedTtl).WithoutFailSafe().WithoutEagerRefresh();
    }

    /// <inheritdoc/>
    public async Task<TenantDescriptor?> GetByIdAsync(Guid tenantId, CancellationToken ct) =>
        await _cache.GetOrSetAsync(
            ByIdKey(tenantId),
            async token => await _inner.GetByIdAsync(tenantId, token).ConfigureAwait(false),
            _policy,
            ct).ConfigureAwait(false);

    /// <inheritdoc/>
    /// <remarks>
    /// Caches only the key-to-tenant mapping; the descriptor itself is read through
    /// <see cref="GetByIdAsync"/>, so <see cref="InvalidateTenantAsync"/> takes effect for lookups by
    /// resolution key too.
    /// </remarks>
    public async Task<TenantDescriptor?> GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionKey);

        TenantDescriptor? resolved = null;
        Guid? tenantId = await _cache.GetOrSetAsync<Guid?>(
            _keyProvider.BuildKey(ByResolutionKeyEntity, resolutionKey),
            async token =>
            {
                resolved = await _inner.GetByResolutionKeyAsync(resolutionKey, token).ConfigureAwait(false);
                if (resolved is not null)
                {
                    await _cache.SetAsync(ByIdKey(resolved.TenantId), resolved, _policy, token).ConfigureAwait(false);
                }

                return resolved?.TenantId;
            },
            _policy,
            ct).ConfigureAwait(false);

        if (tenantId is null)
        {
            return null;
        }

        return resolved?.TenantId == tenantId ? resolved : await GetByIdAsync(tenantId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the cached descriptor for <paramref name="tenantId"/> immediately, bypassing the TTL.
    /// Call this right after changing a tenant's status.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to evict.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the entry is removed.</returns>
    public async Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct) =>
        await _cache.RemoveAsync(ByIdKey(tenantId), ct).ConfigureAwait(false);

    private string ByIdKey(Guid tenantId) => _keyProvider.BuildKey(ByIdEntity, tenantId.ToString("D"));
}
