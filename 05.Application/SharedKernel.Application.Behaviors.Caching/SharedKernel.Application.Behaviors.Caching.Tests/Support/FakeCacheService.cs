using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>
/// An in-memory <see cref="ICacheService"/> double sufficient for behavior-level tests. Honours
/// <see cref="CacheFactoryContext.SkipCaching"/> and tags, and records every call and policy it receives.
/// </summary>
internal sealed class FakeCacheService : ICacheService
{
    private readonly Dictionary<string, (object? Value, IReadOnlyList<string> Tags)> _entries = new(StringComparer.Ordinal);

    public List<string> TryGetCalls { get; } = [];
    public List<string> SetCalls { get; } = [];
    public List<(string Key, CachePolicy Policy)> ContextGetOrSetCalls { get; } = [];
    public List<string> PlainGetOrSetCalls { get; } = [];
    public List<string> RemoveCalls { get; } = [];
    public List<string> RemoveByTagCalls { get; } = [];

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public IReadOnlyList<string>? GetTags(string key) => _entries.TryGetValue(key, out var entry) ? entry.Tags : null;

    public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        TryGetCalls.Add(key);
        return ValueTask.FromResult(Lookup<T>(key));
    }

    public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
    {
        PlainGetOrSetCalls.Add(key);
        return GetOrSetCoreAsync(key, (_, token) => factory(token), policy, ct);
    }

    public ValueTask<T> GetOrSetAsync<T>(string key, Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
    {
        ContextGetOrSetCalls.Add((key, policy));
        return GetOrSetCoreAsync(key, factory, policy, ct);
    }

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        SetCalls.Add(key);
        _entries[key] = (value, policy.Tags);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        RemoveCalls.Add(key);
        _entries.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask ExpireAsync(string key, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        RemoveByTagCalls.Add(tag);
        foreach (var key in _entries.Where(e => e.Value.Tags.Contains(tag, StringComparer.Ordinal)).Select(e => e.Key).ToList())
            _entries.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask ClearAsync(CancellationToken ct = default)
        => throw new NotSupportedException();

    private async ValueTask<T> GetOrSetCoreAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct)
    {
        if (Lookup<T>(key).TryGetValue(out var cached))
            return cached;

        var context = new CacheFactoryContext(key, policy);
        var value = await factory(context, ct).ConfigureAwait(false);

        if (!context.IsCachingSkipped)
        {
            SetCalls.Add(key);
            _entries[key] = (value, policy.Tags);
        }

        return value;
    }

    private CacheLookup<T> Lookup<T>(string key) =>
        _entries.TryGetValue(key, out var entry) && entry.Value is T typed
            ? CacheLookup<T>.Hit(typed)
            : CacheLookup<T>.Miss;
}
