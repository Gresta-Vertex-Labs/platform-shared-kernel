using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ICacheInvalidationBus"/> for use in unit tests.
/// Thread-safe, zero dependency on <c>IRedisChannelService</c> or any Redis package.
/// </summary>
/// <remarks>
/// Records every published <see cref="CacheInvalidationMessage"/> regardless of whether the test
/// ever asserts on it. Registered handlers (<see cref="OnInvalidation"/>) are invoked synchronously
/// on every publish call so a test can chain a <see cref="FakeCacheService.RemoveAsync"/> as the
/// downstream effect without async plumbing of its own.
/// </remarks>
public sealed class FakeCacheInvalidationBus : ICacheInvalidationBus
{
    private readonly ConcurrentQueue<CacheInvalidationMessage> _published = new();
    private readonly ConcurrentQueue<Func<CacheInvalidationMessage, ValueTask>> _handlers = new();

    /// <summary>Gets every <see cref="CacheInvalidationMessage"/> published so far, in publish order.</summary>
    public IReadOnlyList<CacheInvalidationMessage> PublishedInvalidations => _published.ToArray();

    /// <inheritdoc />
    public ValueTask PublishKeyInvalidationAsync(string[] keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return PublishInvalidationAsync(
            new CacheInvalidationMessage("test-svc", CacheInvalidationType.Key, keys: keys),
            ct);
    }

    /// <inheritdoc />
    public ValueTask PublishTagInvalidationAsync(string[] tags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return PublishInvalidationAsync(
            new CacheInvalidationMessage("test-svc", CacheInvalidationType.Tag, tags: tags),
            ct);
    }

    /// <inheritdoc />
    public ValueTask PublishBroadcastInvalidationAsync(CancellationToken ct = default) =>
        PublishInvalidationAsync(new CacheInvalidationMessage("test-svc", CacheInvalidationType.All), ct);

    /// <inheritdoc />
    public async ValueTask PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _published.Enqueue(message);

        foreach (var handler in _handlers)
            await handler(message).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a handler invoked synchronously on every publish call (any of the four
    /// publish overloads).
    /// </summary>
    /// <param name="handler">The handler to invoke on every published invalidation message.</param>
    public void OnInvalidation(Func<CacheInvalidationMessage, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Enqueue(handler);
    }

    /// <summary>Clears all recorded messages and registered handlers.</summary>
    public void Reset()
    {
        _published.Clear();
        _handlers.Clear();
    }
}
