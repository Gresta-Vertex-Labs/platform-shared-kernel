using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ITenantCacheService"/> for use in unit tests.
/// </summary>
/// <remarks>
/// Built on a <see cref="FakeCacheService"/> with the real <see cref="CacheKeyFormat"/> keys and
/// <see cref="CachePolicy.ForTenant"/> tag scoping, so tenant isolation, tenant tags and
/// <see cref="RemoveTenantAsync"/> behave as in production. Inspect the stored entries through
/// <see cref="Cache"/>.
/// </remarks>
public sealed class FakeTenantCacheService : ITenantCacheService
{
    private readonly ITenantCacheKeyProvider _keyProvider;

    /// <summary>Creates a fake over a new <see cref="FakeCacheService"/> and <see cref="FakeTenantCacheKeyProvider"/>.</summary>
    public FakeTenantCacheService()
        : this(new FakeCacheService(), new FakeTenantCacheKeyProvider())
    {
    }

    /// <summary>Creates a fake over the given cache and key provider.</summary>
    /// <param name="cache">The fake cache that stores the entries.</param>
    /// <param name="keyProvider">Builds the tenant keys.</param>
    public FakeTenantCacheService(FakeCacheService cache, ITenantCacheKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(keyProvider);

        Cache = cache;
        _keyProvider = keyProvider;
    }

    /// <summary>Gets the underlying fake cache holding every tenant entry.</summary>
    public FakeCacheService Cache { get; }

    /// <summary>Gets the number of entries held, across all tenants.</summary>
    public int Count => Cache.Count;

    /// <inheritdoc />
    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string tenantId, string entity, string id, CancellationToken ct = default) =>
        Cache.TryGetAsync<T>(_keyProvider.BuildTenantKey(tenantId, entity, id), ct);

    /// <inheritdoc />
    public ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return Cache.GetOrSetAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), factory, policy.ForTenant(tenantId), ct);
    }

    /// <inheritdoc />
    public ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return Cache.GetOrSetAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), factory, policy.ForTenant(tenantId), ct);
    }

    /// <inheritdoc />
    public ValueTask SetAsync<T>(string tenantId, string entity, string id, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return Cache.SetAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), value, policy.ForTenant(tenantId), ct);
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string tenantId, string entity, string id, CancellationToken ct = default) =>
        Cache.RemoveAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), ct);

    /// <inheritdoc />
    public ValueTask ExpireAsync(string tenantId, string entity, string id, CancellationToken ct = default) =>
        Cache.ExpireAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), ct);

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tenantId, string tag, CancellationToken ct = default) =>
        Cache.RemoveByTagAsync(CacheKeyFormat.BuildTenantTag(tenantId, tag), ct);

    /// <inheritdoc />
    public ValueTask RemoveTenantAsync(string tenantId, CancellationToken ct = default) =>
        Cache.RemoveByTagAsync(CacheKeyFormat.BuildTenantWideTag(tenantId), ct);

    /// <summary>Removes every entry for every tenant.</summary>
    public void Reset() => Cache.Clear();
}
