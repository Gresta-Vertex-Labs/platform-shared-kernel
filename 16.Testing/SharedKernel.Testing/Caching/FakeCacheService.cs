using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ICacheService"/> for use in unit tests.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// All operations are synchronous in terms of observable state — calls complete as
/// <see cref="ValueTask.CompletedTask"/> without any background work. TTL and tag
/// semantics from <see cref="CachePolicy"/> are intentionally not enforced: this fake
/// is designed for behavioural correctness testing, not expiry timing.
/// </para>
/// <para>
/// Tags are tracked per key so that <see cref="RemoveByTagAsync"/> correctly evicts
/// all entries that were stored with a given tag.
/// </para>
/// </remarks>
public sealed class FakeCacheService : ICacheService
{
    // Stores boxed values keyed by cache key.
    private readonly ConcurrentDictionary<string, object?> _store = new();

    // Maps each key to its set of tags for tag-based eviction.
    private readonly ConcurrentDictionary<string, HashSet<string>> _keyTags = new();

    /// <summary>
    /// Gets the total number of entries currently held in the fake cache.
    /// Useful for asserting side-effects of batch operations in tests.
    /// </summary>
    public int Count => _store.Count;

    /// <inheritdoc />
    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_store.TryGetValue(key, out var boxed) && boxed is T typed)
            return ValueTask.FromResult<T?>(typed);

        return ValueTask.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        _store[key] = value;

        if (policy.Tags.Length > 0)
            _keyTags[key] = [.. policy.Tags];
        else
            _keyTags.TryRemove(key, out _);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        if (_store.TryGetValue(key, out var boxed) && boxed is T typed)
            return ValueTask.FromResult(typed);

        return GetOrSetInternalAsync(key, factory, policy, ct);
    }

    private async ValueTask<T> GetOrSetInternalAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct)
    {
        var value = await factory(ct).ConfigureAwait(false);
        _store[key] = value;

        if (policy.Tags.Length > 0)
            _keyTags[key] = [.. policy.Tags];

        return value;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _store.TryRemove(key, out _);
        _keyTags.TryRemove(key, out _);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var keysToRemove = _keyTags
            .Where(kvp => kvp.Value.Contains(tag))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _store.TryRemove(key, out _);
            _keyTags.TryRemove(key, out _);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every requested key is present in the returned dictionary.
    /// Keys not found in the fake cache map to <see langword="null"/>.
    /// An empty <paramref name="keys"/> enumerable returns an empty dictionary immediately.
    /// </remarks>
    public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<string, T?>();

        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (_store.TryGetValue(key, out var boxed) && boxed is T typed)
                result[key] = typed;
            else
                result[key] = default;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, T?>>(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same <paramref name="policy"/> is applied to every entry — identical to calling
    /// <see cref="SetAsync{T}"/> for each key individually with the same policy.
    /// An empty <paramref name="entries"/> dictionary is a no-op.
    /// </remarks>
    public ValueTask SetManyAsync<T>(
        IReadOnlyDictionary<string, T> entries,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        foreach (var (key, value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            _store[key] = value;

            if (policy.Tags.Length > 0)
                _keyTags[key] = [.. policy.Tags];
            else
                _keyTags.TryRemove(key, out _);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears all entries from the fake cache. Useful for test isolation when the same
    /// instance is reused across multiple test cases.
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _keyTags.Clear();
    }
}
