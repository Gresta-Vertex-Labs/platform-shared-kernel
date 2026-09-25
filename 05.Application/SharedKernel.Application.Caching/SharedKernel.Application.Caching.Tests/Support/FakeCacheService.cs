using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Caching.Tests.Support;

/// <summary>
/// An in-memory <see cref="ICacheService"/> double for behavior-level tests.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the stampede semantics measured against a real FusionCache, because the caching
/// behavior's failure path depends on them: one caller runs the factory under a per-key lock, and a
/// concurrent caller re-checks the entry after acquiring that lock, so it is served the stored value
/// on the success path and runs the factory itself when the first caller called
/// <see cref="CacheFactoryContext.SkipCaching"/> and stored nothing. A fake that simply ran the
/// factory on every call, as the earlier one did, makes both paths look identical and cannot fail on
/// a regression in either.
/// </para>
/// <para>
/// <see cref="FailEvictionsFor"/> makes eviction throw, so the post-commit "one failure must not
/// abandon the rest" path is reachable from a test.
/// </para>
/// </remarks>
internal sealed class FakeCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failingEvictions = new(StringComparer.Ordinal);

    private readonly record struct Entry(object? Value, IReadOnlyList<string> Tags);

    public List<string> TryGetCalls { get; } = [];
    public List<string> SetCalls { get; } = [];
    public List<(string Key, CachePolicy Policy)> ContextGetOrSetCalls { get; } = [];
    public List<string> PlainGetOrSetCalls { get; } = [];
    public List<string> RemoveCalls { get; } = [];
    public List<string> RemoveByTagCalls { get; } = [];

    private int _factoryInvocations;

    public int FactoryInvocations => Volatile.Read(ref _factoryInvocations);

    public IReadOnlyCollection<string> Keys => _entries.Keys.ToArray();

    public IReadOnlyList<string>? GetTags(string key) => _entries.TryGetValue(key, out var entry) ? entry.Tags : null;

    public void FailEvictionsFor(string target) => _failingEvictions.Add(target);

    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        TryGetCalls.Add(key);
        return ValueTask.FromResult(Lookup<T>(key));
    }

    public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
    {
        lock (PlainGetOrSetCalls)
        {
            PlainGetOrSetCalls.Add(key);
        }

        return GetOrSetCoreAsync(key, (_, token) => factory(token), policy, ct);
    }

    public ValueTask<T> GetOrSetAsync<T>(string key, Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
    {
        lock (ContextGetOrSetCalls)
        {
            ContextGetOrSetCalls.Add((key, policy));
        }

        return GetOrSetCoreAsync(key, factory, policy, ct);
    }

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        lock (SetCalls)
        {
            SetCalls.Add(key);
        }

        _entries[key] = new Entry(value, policy.Tags);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        RemoveCalls.Add(key);
        if (_failingEvictions.Contains(key))
            throw new InvalidOperationException("Injected eviction failure for key " + key);

        _entries.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask ExpireAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        RemoveByTagCalls.Add(tag);
        if (_failingEvictions.Contains(tag))
            throw new InvalidOperationException("Injected eviction failure for tag " + tag);

        foreach (var key in _entries.Where(e => e.Value.Tags.Contains(tag, StringComparer.Ordinal)).Select(e => e.Key).ToList())
            _entries.TryRemove(key, out _);

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => throw new NotSupportedException();

    public ValueTask ClearAsync(CancellationToken ct = default) => throw new NotSupportedException();

    private async ValueTask<T> GetOrSetCoreAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct)
    {
        if (Lookup<T>(key).TryGetValue(out var cached))
            return cached;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The re-check after the lock is what makes a concurrent caller a hit on the success
            // path and a second factory run on the skip path, matching the real cache.
            if (Lookup<T>(key).TryGetValue(out var afterLock))
                return afterLock;

            Interlocked.Increment(ref _factoryInvocations);
            var context = new CacheFactoryContext(key, policy);
            var value = await factory(context, ct).ConfigureAwait(false);

            if (!context.IsCachingSkipped)
            {
                lock (SetCalls)
                {
                    SetCalls.Add(key);
                }

                _entries[key] = new Entry(value, policy.Tags);
            }

            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    private CacheLookup<T> Lookup<T>(string key) =>
        _entries.TryGetValue(key, out var entry) && entry.Value is T typed
            ? CacheLookup<T>.Hit(typed)
            : CacheLookup<T>.Miss;
}
