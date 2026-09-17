using Microsoft.Extensions.Caching.Distributed;
using SharedKernel.Caching.Redis.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests.Integration;

/// <summary>
/// <see cref="RedisDistributedCache"/> against a real Redis: one string per entry under
/// <c>{KeyPrefix}{key}</c>, expiry mapped to a TTL, sliding expiration rejected, refresh a no-op.
/// </summary>
[Collection("Redis")]
public sealed class RedisDistributedCacheTests : IAsyncLifetime
{
    private static readonly byte[] Value = [1, 2, 3, 4];

    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

    private ConnectionMultiplexer? _multiplexer;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
            await _multiplexer.DisposeAsync();

        await _container.DisposeAsync();
    }

    private IDatabase Db => _multiplexer!.GetDatabase();

    private RedisDistributedCache Cache(string keyPrefix = "") => new(_multiplexer!, keyPrefix, _time);

    private static string NewKey() => "dc:" + Guid.NewGuid().ToString("N");

    // ─── Key format ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_WritesOneStringUnderThePrefixedKey()
    {
        var key = NewKey();

        await Cache("tenant-a:").SetAsync(key, Value, new DistributedCacheEntryOptions());

        Assert.Equal(RedisType.String, await Db.KeyTypeAsync("tenant-a:" + key));
        Assert.Equal(Value, (byte[]?)await Db.StringGetAsync("tenant-a:" + key));
        Assert.False(await Db.KeyExistsAsync(key));
        Assert.False(await Db.KeyExistsAsync("tenant-a:v2:" + key));
    }

    [Fact]
    public async Task EmptyPrefix_WritesTheRawKey()
    {
        var key = NewKey();

        Cache().Set(key, Value, new DistributedCacheEntryOptions());

        Assert.Equal(RedisType.String, await Db.KeyTypeAsync(key));
    }

    [Fact]
    public async Task DifferentPrefixes_DoNotSeeEachOthersEntries()
    {
        var key = NewKey();
        await Cache("a:").SetAsync(key, Value, new DistributedCacheEntryOptions());

        Assert.Null(await Cache("b:").GetAsync(key));
        Assert.Equal(Value, await Cache("a:").GetAsync(key));

        await Cache("b:").RemoveAsync(key);
        Assert.True(await Db.KeyExistsAsync("a:" + key));
    }

    // ─── Expiry ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RelativeExpiration_SetsTheTtl_Async()
    {
        var key = NewKey();

        await Cache("p:").SetAsync(key, Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });

        AssertTtlBetween(await Db.KeyTimeToLiveAsync("p:" + key), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task RelativeExpiration_SetsTheTtl_Sync()
    {
        var key = NewKey();

        Cache("p:").Set(key, Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) });

        AssertTtlBetween(await Db.KeyTimeToLiveAsync("p:" + key), TimeSpan.FromSeconds(115), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task AbsoluteExpiration_SetsTheTtlRelativeToTheTimeProvider()
    {
        var key = NewKey();
        var options = new DistributedCacheEntryOptions { AbsoluteExpiration = _time.GetUtcNow().AddSeconds(45) };

        await Cache().SetAsync(key, Value, options);

        // The TTL comes from the injected clock, not the machine clock (which is far from 2026-09-17 12:00).
        AssertTtlBetween(await Db.KeyTimeToLiveAsync(key), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task AbsoluteExpiration_Sync_SetsTheTtl()
    {
        var key = NewKey();

        Cache().Set(key, Value, new DistributedCacheEntryOptions { AbsoluteExpiration = _time.GetUtcNow().AddMinutes(10) });

        AssertTtlBetween(await Db.KeyTimeToLiveAsync(key), TimeSpan.FromSeconds(595), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task RelativeExpiration_WinsOverAbsoluteExpiration()
    {
        var key = NewKey();
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = _time.GetUtcNow().AddHours(1),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20),
        };

        await Cache().SetAsync(key, Value, options);

        AssertTtlBetween(await Db.KeyTimeToLiveAsync(key), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public async Task AbsoluteExpirationNowOrInThePast_DeletesTheEntry_Async(int secondsFromNow)
    {
        var key = NewKey();
        await Db.StringSetAsync(key, "existing");

        await Cache().SetAsync(key, Value, new DistributedCacheEntryOptions { AbsoluteExpiration = _time.GetUtcNow().AddSeconds(secondsFromNow) });

        Assert.False(await Db.KeyExistsAsync(key));
    }

    // Regression: StackExchange.Redis truncates a sub-millisecond expiry to 0, which Redis rejects with an error,
    // leaving the stale value in place. Under 1 ms counts as already expired.
    [Fact]
    public async Task ExpiryUnderOneMillisecond_DeletesTheEntry_InsteadOfFailing()
    {
        var key = NewKey();
        await Db.StringSetAsync(key, "existing");

        await Cache().SetAsync(key, Value, new DistributedCacheEntryOptions { AbsoluteExpiration = _time.GetUtcNow().AddTicks(5_000) });
        Assert.False(await Db.KeyExistsAsync(key));

        await Db.StringSetAsync(key, "existing");
        Cache().Set(key, Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromTicks(1) });
        Assert.False(await Db.KeyExistsAsync(key));
    }

    [Fact]
    public async Task AbsoluteExpirationInThePast_DeletesTheEntry_Sync()
    {
        var key = NewKey();
        await Db.StringSetAsync("p:" + key, "existing");

        Cache("p:").Set(key, Value, new DistributedCacheEntryOptions { AbsoluteExpiration = _time.GetUtcNow().AddMinutes(-5) });

        Assert.False(await Db.KeyExistsAsync("p:" + key));
    }

    [Fact]
    public async Task NoExpiration_PersistsWithoutTtl()
    {
        var key = NewKey();

        await Cache().SetAsync(key, Value, new DistributedCacheEntryOptions());

        Assert.True(await Db.KeyExistsAsync(key));
        Assert.Null(await Db.KeyTimeToLiveAsync(key));
    }

    [Fact]
    public async Task NoExpiration_OverwritingAnEntryWithTtl_RemovesTheTtl()
    {
        var key = NewKey();
        var cache = Cache();
        await cache.SetAsync(key, [9], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });

        await cache.SetAsync(key, Value, new DistributedCacheEntryOptions());

        Assert.Null(await Db.KeyTimeToLiveAsync(key));
        Assert.Equal(Value, await cache.GetAsync(key));
    }

    [Fact]
    public async Task ExpiredEntry_IsGoneAfterItsTtl()
    {
        var key = NewKey();
        var cache = Cache();

        await cache.SetAsync(key, Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(200) });
        Assert.Equal(Value, await cache.GetAsync(key));

        await Task.Delay(600);

        Assert.Null(await cache.GetAsync(key));
    }

    [Fact]
    public async Task SlidingExpiration_Throws_AndWritesNothing()
    {
        var key = NewKey();
        var cache = Cache();
        var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(1) };

        Assert.Throws<NotSupportedException>(() => cache.Set(key, Value, options));
        await Assert.ThrowsAsync<NotSupportedException>(() => cache.SetAsync(key, Value, options));

        var combined = new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(1),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        };
        await Assert.ThrowsAsync<NotSupportedException>(() => cache.SetAsync(key, Value, combined));

        Assert.False(await Db.KeyExistsAsync(key));
    }

    // ─── Get, Remove, Refresh ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsTheStoredBytes_OrNullWhenMissing()
    {
        var key = NewKey();
        var cache = Cache("g:");

        Assert.Null(cache.Get(key));
        Assert.Null(await cache.GetAsync(key));

        await cache.SetAsync(key, Value, new DistributedCacheEntryOptions());

        Assert.Equal(Value, cache.Get(key));
        Assert.Equal(Value, await cache.GetAsync(key));
    }

    [Fact]
    public async Task Get_EmptyValue_RoundTrips()
    {
        var key = NewKey();
        var cache = Cache();

        await cache.SetAsync(key, [], new DistributedCacheEntryOptions());

        var stored = await cache.GetAsync(key);
        Assert.NotNull(stored);
        Assert.Empty(stored);
    }

    [Fact]
    public async Task Remove_DeletesThePrefixedKey_SyncAndAsync()
    {
        var first = NewKey();
        var second = NewKey();
        var cache = Cache("r:");
        await cache.SetAsync(first, Value, new DistributedCacheEntryOptions());
        await cache.SetAsync(second, Value, new DistributedCacheEntryOptions());

        cache.Remove(first);
        await cache.RemoveAsync(second);

        Assert.False(await Db.KeyExistsAsync("r:" + first));
        Assert.False(await Db.KeyExistsAsync("r:" + second));

        // Removing a missing key is not an error.
        await cache.RemoveAsync(NewKey());
    }

    [Fact]
    public async Task Refresh_IsANoOp_AndLeavesTheTtlAlone()
    {
        var key = NewKey();
        var cache = Cache();
        await cache.SetAsync(key, Value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) });
        await Db.KeyExpireAsync(key, TimeSpan.FromSeconds(10));

        cache.Refresh(key);
        await cache.RefreshAsync(key);
        await cache.RefreshAsync(NewKey());

        AssertTtlBetween(await Db.KeyTimeToLiveAsync(key), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    // ─── Cancellation and arguments ───────────────────────────────────────────────

    [Fact]
    public async Task CanceledToken_IsCheckedBeforeAnyRedisCall()
    {
        var key = NewKey();
        var cache = Cache();
        await Db.StringSetAsync(key, "existing");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(key, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.SetAsync(key, Value, new DistributedCacheEntryOptions(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.RemoveAsync(key, cts.Token));

        Assert.Equal("existing", (string?)await Db.StringGetAsync(key));
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        var cache = Cache();

        Assert.Throws<ArgumentNullException>(() => cache.Get(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.GetAsync(null!));
        Assert.Throws<ArgumentNullException>(() => cache.Set(null!, Value, new DistributedCacheEntryOptions()));
        Assert.Throws<ArgumentNullException>(() => cache.Set(NewKey(), null!, new DistributedCacheEntryOptions()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.SetAsync(NewKey(), null!, new DistributedCacheEntryOptions()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.SetAsync(NewKey(), Value, null!));
        Assert.Throws<ArgumentNullException>(() => cache.Remove(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.RemoveAsync(null!));
    }

    private static void AssertTtlBetween(TimeSpan? ttl, TimeSpan min, TimeSpan max)
    {
        Assert.NotNull(ttl);
        Assert.True(ttl >= min && ttl <= max, $"Expected a TTL between {min} and {max}, got {ttl}.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
