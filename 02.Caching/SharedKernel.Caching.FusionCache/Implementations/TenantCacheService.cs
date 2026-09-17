using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// <see cref="ITenantCacheService"/> over <see cref="ICacheService"/>: builds every key with
/// <see cref="ITenantCacheKeyProvider.BuildTenantKey"/> and scopes every policy with
/// <see cref="CachePolicy.ForTenant"/>, so each entry also carries its tenant-wide tag.
/// </summary>
internal sealed class TenantCacheService : ITenantCacheService
{
    private readonly ICacheService _cache;
    private readonly ITenantCacheKeyProvider _keyProvider;

    public TenantCacheService(ICacheService cache, ITenantCacheKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(keyProvider);

        _cache = cache;
        _keyProvider = keyProvider;
    }

    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string tenantId, string entity, string id, CancellationToken ct = default) =>
        _cache.TryGetAsync<T>(_keyProvider.BuildTenantKey(tenantId, entity, id), ct);

    public ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        string key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        return _cache.GetOrSetAsync(key, factory, policy.ForTenant(tenantId), ct);
    }

    public ValueTask<T> GetOrSetAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        string key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        return _cache.GetOrSetAsync(key, factory, policy.ForTenant(tenantId), ct);
    }

    public ValueTask SetAsync<T>(string tenantId, string entity, string id, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        string key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        return _cache.SetAsync(key, value, policy.ForTenant(tenantId), ct);
    }

    public ValueTask RemoveAsync(string tenantId, string entity, string id, CancellationToken ct = default) =>
        _cache.RemoveAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), ct);

    public ValueTask ExpireAsync(string tenantId, string entity, string id, CancellationToken ct = default) =>
        _cache.ExpireAsync(_keyProvider.BuildTenantKey(tenantId, entity, id), ct);

    public ValueTask RemoveByTagAsync(string tenantId, string tag, CancellationToken ct = default) =>
        _cache.RemoveByTagAsync(CacheKeyFormat.BuildTenantTag(tenantId, tag), ct);

    public ValueTask RemoveTenantAsync(string tenantId, CancellationToken ct = default) =>
        _cache.RemoveByTagAsync(CacheKeyFormat.BuildTenantWideTag(tenantId), ct);
}
