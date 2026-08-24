using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// Default <see cref="ITenantCacheService"/> implementation. Composes <see cref="ICacheService"/>
/// and <see cref="ITenantCacheKeyProvider"/> — every method builds the tenant-scoped key via
/// <see cref="ITenantCacheKeyProvider.BuildTenantKey"/> and delegates to the wrapped
/// <see cref="ICacheService"/>. Carries zero duplicated key-formatting logic.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tag tenant-scoping.</strong> <see cref="SetAsync{T}"/> rewrites every tag on the
/// supplied <see cref="CachePolicy"/> to <c>{tenantId}:{tag}</c> before delegating to
/// <see cref="ICacheService.SetAsync{T}"/>, and <see cref="RemoveByTagAsync"/> applies the
/// identical rewrite before calling <see cref="ICacheService.RemoveByTagAsync"/>. FusionCache
/// tags live in a global namespace that <see cref="ITenantCacheKeyProvider"/> never touches —
/// without this rewrite, two tenants both tagging <c>"orders"</c> would share one FusionCache
/// tag, and one tenant's <see cref="RemoveByTagAsync"/> call would invalidate the other
/// tenant's <c>"orders"</c>-tagged entries too.
/// </para>
/// <para>
/// Zero dependency on <c>12.Security</c> or <c>IHttpContextAccessor</c> — <c>tenantId</c> is
/// always the caller's explicit argument.
/// </para>
/// </remarks>
internal sealed class TenantCacheService : ITenantCacheService
{
    private readonly ICacheService _cache;
    private readonly ITenantCacheKeyProvider _keyProvider;

    /// <summary>
    /// Initialises a new instance of <see cref="TenantCacheService"/>.
    /// </summary>
    /// <param name="cache">The wrapped, non-tenant-aware cache service.</param>
    /// <param name="keyProvider">The tenant-aware key provider used to build every key.</param>
    public TenantCacheService(ICacheService cache, ITenantCacheKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(keyProvider);

        _cache = cache;
        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public ValueTask<T?> GetAsync<T>(string tenantId, string entity, string id, CancellationToken ct = default)
    {
        var key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        return _cache.GetAsync<T>(key, ct);
    }

    /// <inheritdoc />
    public ValueTask SetAsync<T>(
        string tenantId,
        string entity,
        string id,
        T value,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // BuildTenantKey validates tenantId (throws ArgumentException on null/whitespace) before
        // ScopeTagsToTenant below ever reads it, so no separate tenantId guard is needed here.
        var key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        var scopedPolicy = ScopeTagsToTenant(policy, tenantId);

        return _cache.SetAsync(key, value, scopedPolicy, ct);
    }

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

        // BuildTenantKey validates tenantId (throws ArgumentException on null/whitespace) before
        // ScopeTagsToTenant below ever reads it, so no separate tenantId guard is needed here.
        var key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        var scopedPolicy = ScopeTagsToTenant(policy, tenantId);

        return _cache.GetOrSetAsync(key, factory, scopedPolicy, ct);
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string tenantId, string entity, string id, CancellationToken ct = default)
    {
        var key = _keyProvider.BuildTenantKey(tenantId, entity, id);
        return _cache.RemoveAsync(key, ct);
    }

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tenantId, string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var scopedTag = ScopeTagToTenant(tenantId, tag);
        return _cache.RemoveByTagAsync(scopedTag, ct);
    }

    /// <summary>
    /// Returns a copy of <paramref name="policy"/> with every tag rewritten to its tenant-scoped
    /// form (<c>{tenantId}:{tag}</c>). Returns <paramref name="policy"/> unchanged when it carries
    /// no tags — avoids an unnecessary record copy on the (common) untagged path.
    /// </summary>
    private static CachePolicy ScopeTagsToTenant(CachePolicy policy, string tenantId)
    {
        if (policy.Tags.Length == 0)
            return policy;

        var scopedTags = new string[policy.Tags.Length];
        for (var i = 0; i < policy.Tags.Length; i++)
            scopedTags[i] = ScopeTagToTenant(tenantId, policy.Tags[i]);

        return policy.WithTags(scopedTags);
    }

    /// <summary>Rewrites a single tag to its tenant-scoped form: <c>{tenantId}:{tag}</c>.</summary>
    private static string ScopeTagToTenant(string tenantId, string tag) => $"{tenantId}:{tag}";
}
