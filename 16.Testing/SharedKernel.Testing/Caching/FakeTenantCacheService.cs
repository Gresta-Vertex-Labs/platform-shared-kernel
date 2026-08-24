using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ITenantCacheService"/> for use in unit tests.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Backed by a composite <c>(TenantId, Entity, Id)</c> tuple key — two different
/// <c>tenantId</c> values sharing an identical <c>(entity, id)</c> pair can never collide,
/// so cross-tenant read/write isolation holds by construction, not by a runtime check.
/// </para>
/// <para>
/// Tag-based invalidation is independently tenant-scoped: tags are tracked per
/// <c>(TenantId, Tag)</c>, mirroring the real (planned) production implementation's own
/// documented tag-rewrite rule — <see cref="RemoveByTagAsync"/> can never evict another
/// tenant's entries even when two tenants both use the same tag name.
/// </para>
/// <para>
/// All operations are synchronous in terms of observable state — calls complete without any
/// background work. TTL semantics from <see cref="CachePolicy"/> are intentionally not
/// enforced: this fake is designed for behavioural correctness testing, not expiry timing.
/// </para>
/// <para>
/// This is a deliberately independent fake with its own backing store — it does not wrap or
/// delegate to <see cref="FakeCacheService"/>.
/// </para>
/// </remarks>
public sealed class FakeTenantCacheService : ITenantCacheService
{
    // Stores boxed values keyed by the composite (tenant, entity, id) tuple.
    private readonly ConcurrentDictionary<(string TenantId, string Entity, string Id), object?> _store = new();

    // Tracks the current tag set for each stored key, so a re-Set with different (or no)
    // tags correctly retires the key's prior tag associations below.
    private readonly ConcurrentDictionary<(string TenantId, string Entity, string Id), string[]> _keyTags = new();

    // Maps each tenant-scoped tag to the set of (entity, id) pairs carrying it.
    private readonly ConcurrentDictionary<(string TenantId, string Tag), ConcurrentDictionary<(string Entity, string Id), byte>> _tagIndex = new();

    /// <summary>
    /// Gets the total number of entries currently held in the fake cache, across all tenants.
    /// Useful for asserting side-effects of operations in tests.
    /// </summary>
    public int Count => _store.Count;

    /// <inheritdoc />
    public ValueTask<T?> GetAsync<T>(string tenantId, string entity, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_store.TryGetValue((tenantId, entity, id), out var boxed) && boxed is T typed)
            return ValueTask.FromResult<T?>(typed);

        return ValueTask.FromResult<T?>(default);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(policy);

        StoreWithTags(tenantId, entity, id, value, policy.Tags);

        return ValueTask.CompletedTask;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        if (_store.TryGetValue((tenantId, entity, id), out var boxed) && boxed is T typed)
            return ValueTask.FromResult(typed);

        return GetOrSetInternalAsync(tenantId, entity, id, factory, policy, ct);
    }

    private async ValueTask<T> GetOrSetInternalAsync<T>(
        string tenantId,
        string entity,
        string id,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct)
    {
        var value = await factory(ct).ConfigureAwait(false);
        StoreWithTags(tenantId, entity, id, value, policy.Tags);
        return value;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string tenantId, string entity, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        RemoveKey(tenantId, entity, id);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tenantId, string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_tagIndex.TryRemove((tenantId, tag), out var taggedKeys))
        {
            foreach (var (entity, id) in taggedKeys.Keys)
                RemoveKey(tenantId, entity, id);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears all entries for every tenant from the fake cache. Useful for test isolation
    /// when the same instance is reused across multiple test cases.
    /// </summary>
    public void Reset()
    {
        _store.Clear();
        _keyTags.Clear();
        _tagIndex.Clear();
    }

    // ----- helpers -----

    private void StoreWithTags<T>(string tenantId, string entity, string id, T value, string[] tags)
    {
        var key = (tenantId, entity, id);

        _store[key] = value;
        DetachFromPriorTags(tenantId, key, entity, id);

        if (tags.Length > 0)
        {
            _keyTags[key] = tags;

            foreach (var tag in tags)
            {
                var bucket = _tagIndex.GetOrAdd(
                    (tenantId, tag),
                    static _ => new ConcurrentDictionary<(string Entity, string Id), byte>());
                bucket[(entity, id)] = 0;
            }
        }
        else
        {
            _keyTags.TryRemove(key, out _);
        }
    }

    private void RemoveKey(string tenantId, string entity, string id)
    {
        var key = (tenantId, entity, id);

        _store.TryRemove(key, out _);
        DetachFromPriorTags(tenantId, key, entity, id);
        _keyTags.TryRemove(key, out _);
    }

    private void DetachFromPriorTags(
        string tenantId,
        (string TenantId, string Entity, string Id) key,
        string entity,
        string id)
    {
        if (!_keyTags.TryGetValue(key, out var priorTags))
            return;

        foreach (var priorTag in priorTags)
        {
            if (_tagIndex.TryGetValue((tenantId, priorTag), out var bucket))
                bucket.TryRemove((entity, id), out _);
        }
    }
}
