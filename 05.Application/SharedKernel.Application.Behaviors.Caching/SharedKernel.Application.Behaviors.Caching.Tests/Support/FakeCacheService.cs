using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>An in-memory <see cref="ICacheService"/> double sufficient for behavior-level tests.</summary>
internal sealed class FakeCacheService : ICacheService
{
    private readonly Dictionary<string, object?> _entries = [];
    private readonly HashSet<string> _tagged = []; // "tag|key" pairs

    public List<string> GetCalls { get; } = [];
    public List<string> SetCalls { get; } = [];
    public List<string> RemoveCalls { get; } = [];
    public List<string> RemoveByTagCalls { get; } = [];

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        GetCalls.Add(key);
        return ValueTask.FromResult(_entries.TryGetValue(key, out var value) ? (T?)value : default);
    }

    public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        SetCalls.Add(key);
        _entries[key] = value;
        foreach (var tag in policy.Tags)
            _tagged.Add($"{tag}|{key}");
        return ValueTask.CompletedTask;
    }

    public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
        => throw new NotSupportedException("CachingBehavior deliberately uses GetAsync/SetAsync, never GetOrSetAsync.");

    public ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        RemoveCalls.Add(key);
        _entries.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        RemoveByTagCalls.Add(tag);
        foreach (var pair in _tagged.Where(p => p.StartsWith(tag + "|", StringComparison.Ordinal)).ToList())
        {
            var key = pair[(tag.Length + 1)..];
            _entries.Remove(key);
            _tagged.Remove(pair);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
        => throw new NotSupportedException();

    public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
        => throw new NotSupportedException();
}
