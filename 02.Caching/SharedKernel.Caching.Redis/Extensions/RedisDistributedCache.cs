using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// The distributed layer FusionCache writes to: one Redis string per entry on the shared connection, which it never
/// closes or disposes.
/// </summary>
/// <remarks>
/// <para>
/// Used instead of <c>Microsoft.Extensions.Caching.StackExchangeRedis</c>, whose <c>RedisCache</c> closes the
/// connection it is given when it is disposed. It is handed to FusionCache only and is not registered as
/// <see cref="IDistributedCache"/>, so no other component shares or disposes it.
/// </para>
/// <para>
/// FusionCache sets absolute expirations and keeps its own entry metadata in the payload, so sliding expiration is
/// not supported and <c>Refresh</c> does nothing.
/// </para>
/// </remarks>
internal sealed class RedisDistributedCache(IConnectionMultiplexer multiplexer, string keyPrefix, TimeProvider timeProvider)
    : IDistributedCache
{
    public byte[]? Get(string key) => Database.StringGet(Key(key));

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return await Database.StringGetAsync(Key(key)).ConfigureAwait(false);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        var timeToLive = TimeToLive(options);
        if (timeToLive < MinimumTimeToLive)
            Database.KeyDelete(Key(key));
        else
            Database.StringSet(Key(key), value, timeToLive, When.Always);
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        token.ThrowIfCancellationRequested();

        var timeToLive = TimeToLive(options);
        if (timeToLive < MinimumTimeToLive)
            await Database.KeyDeleteAsync(Key(key)).ConfigureAwait(false);
        else
            await Database.StringSetAsync(Key(key), value, timeToLive, When.Always).ConfigureAwait(false);
    }

    public void Refresh(string key)
    {
        // No sliding expiration: nothing to refresh.
    }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => Database.KeyDelete(Key(key));

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await Database.KeyDeleteAsync(Key(key)).ConfigureAwait(false);
    }

    private IDatabase Database => multiplexer.GetDatabase();

    private RedisKey Key(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return keyPrefix + key;
    }

    // Redis expiries are whole milliseconds; anything shorter is sent as 0, which Redis rejects, so it counts as expired.
    private static readonly TimeSpan MinimumTimeToLive = TimeSpan.FromMilliseconds(1);

    // null means no expiry; under MinimumTimeToLive means already expired.
    private TimeSpan? TimeToLive(DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.SlidingExpiration is not null)
            throw new NotSupportedException("The Redis distributed layer does not support sliding expiration.");

        if (options.AbsoluteExpirationRelativeToNow is { } relative)
            return relative;

        if (options.AbsoluteExpiration is { } absolute)
            return absolute - timeProvider.GetUtcNow();

        return null;
    }
}
