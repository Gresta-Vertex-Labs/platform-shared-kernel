using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ICacheService"/> for use in unit tests.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every call completes synchronously, with no background work. Durations, fail-safe, eager
/// refresh, jitter and timeouts from <see cref="CachePolicy"/> are not simulated, so
/// <see cref="ExpireAsync"/> removes the entry exactly like <see cref="RemoveAsync"/>.
/// </para>
/// <para>
/// What is faithful: a hit versus a miss (including a cached <see langword="null"/>), tags and
/// tag removal, the factory's <see cref="CacheFactoryContext.SkipCaching"/> decision, and stampede
/// protection.
/// </para>
/// <para>
/// Stampede protection is reproduced rather than approximated, because the difference is observable
/// and consumers depend on it: concurrent misses for one key serialise on a per-key gate, and each
/// waiter re-checks the entry after acquiring it. A waiter is therefore served the stored value when
/// the first factory run wrote one, and runs the factory itself when that run called
/// <see cref="CacheFactoryContext.SkipCaching"/> and stored nothing -- the same split the real cache
/// produces, and the one <c>SharedKernel.Application.Caching</c> relies on to carry a
/// failed result out of its factory. A fake that ran the factory on every concurrent miss makes both
/// paths look identical and cannot fail on a regression in either.
/// </para>
/// </remarks>
public sealed class FakeCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _factoryGates = new(StringComparer.Ordinal);

    /// <summary>Gets the number of entries currently held.</summary>
    public int Count => _store.Count;

    /// <summary>Gets how many times a <c>GetOrSetAsync</c> factory has run.</summary>
    public int FactoryInvocationCount => _factoryInvocationCount;

    private int _factoryInvocationCount;

    /// <inheritdoc />
    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ValueTask.FromResult(Lookup<T>(key));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<string, CacheLookup<T>>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(keys));
            result[key] = Lookup<T>(key);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, CacheLookup<T>>>(result);
    }

    /// <inheritdoc />
    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return GetOrSetAsync(key, (_, token) => factory(token), policy, ct);
    }

    /// <inheritdoc />
    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        if (Lookup<T>(key).TryGetValue(out var cached))
            return cached;

        var gate = _factoryGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The re-check after the gate is what makes a concurrent caller a hit when the first
            // factory run stored a value, and a second factory run when it skipped caching.
            if (Lookup<T>(key).TryGetValue(out var afterGate))
                return afterGate;

            Interlocked.Increment(ref _factoryInvocationCount);
            var context = new CacheFactoryContext(key, policy);
            var value = await factory(context, ct).ConfigureAwait(false);

            if (!context.IsCachingSkipped)
                _store[key] = new Entry(value, policy.Tags);

            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        _store[key] = new Entry(value, policy.Tags);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SetManyAsync<T>(
        IReadOnlyDictionary<string, T> entries,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        foreach (var (key, value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(entries));
            _store[key] = new Entry(value, policy.Tags);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>The fake has no fail-safe, so expiring an entry removes it.</remarks>
    public ValueTask ExpireAsync(string key, CancellationToken ct = default) => RemoveAsync(key, ct);

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return RemoveByTagsAsync([tag], ct);
    }

    /// <inheritdoc />
    public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var tagSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag, nameof(tags));
            tagSet.Add(tag);
        }

        foreach (var (key, entry) in _store)
        {
            if (entry.Tags.Any(tagSet.Contains))
                _store.TryRemove(key, out _);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken ct = default)
    {
        Clear();
        return ValueTask.CompletedTask;
    }

    /// <summary>Gets the tags stored with <paramref name="key"/>, or <see langword="null"/> when the key is absent.</summary>
    /// <param name="key">The cache key.</param>
    /// <returns>The entry's tags, or <see langword="null"/>.</returns>
    public IReadOnlyList<string>? GetTags(string key) => _store.TryGetValue(key, out var entry) ? entry.Tags : null;

    /// <summary>Removes every entry and resets <see cref="FactoryInvocationCount"/>.</summary>
    public void Clear()
    {
        _store.Clear();
        Interlocked.Exchange(ref _factoryInvocationCount, 0);
    }

    private CacheLookup<T> Lookup<T>(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return CacheLookup<T>.Miss;

        return entry.Value switch
        {
            T typed => CacheLookup<T>.Hit(typed),
            null when default(T) is null => CacheLookup<T>.Hit(default!),
            _ => CacheLookup<T>.Miss,
        };
    }

    private sealed record Entry(object? Value, IReadOnlyList<string> Tags);
}
