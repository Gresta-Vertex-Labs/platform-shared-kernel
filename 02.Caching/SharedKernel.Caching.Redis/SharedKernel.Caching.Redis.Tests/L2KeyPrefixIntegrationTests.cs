using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Integration tests that verify the effective Redis key format when <c>KeyPrefix</c>
/// is configured on <see cref="RedisL2Options"/>.
///
/// <para>
/// Raw Redis key inspection is performed via <see cref="IConnectionMultiplexer"/> so that
/// the test fails if the prefix is absent, double-applied, or malformed.
/// </para>
///
/// <para>
/// Verified effective L2 key format: <c>{KeyPrefix}v2:{user-key}</c>, one Redis string per entry. The distributed
/// layer (<c>RedisDistributedCache</c>) writes <c>{KeyPrefix}{key it is given}</c>; the <c>v2:</c> segment is
/// FusionCache's own wire-format marker (<c>FusionCacheOptions.DistributedCacheKeyModifierMode</c>, default
/// <c>Prefix</c>), prepended before the key reaches the distributed layer.
/// </para>
/// </summary>
[Collection("Redis")]
public sealed class L2KeyPrefixIntegrationTests : IAsyncLifetime
{
    private const string KeyPrefix = "myservice";
    private const string UserKey = "prefix-test:entry:1";

    /// <summary>FusionCache's distributed wire-format marker, prepended to every key it hands the distributed layer.</summary>
    private const string FusionCacheWireFormatSegment = "v2:";

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine").Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = _redisContainer.GetConnectionString());
        services.AddSharedKernelCaching(o => o.ServiceName = "prefix-test-svc")
                .AddRedisL2(o => o.KeyPrefix = KeyPrefix);

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    private ICacheService Cache => _provider!.GetRequiredService<ICacheService>();
    private IConnectionMultiplexer Multiplexer => _provider!.GetRequiredService<IConnectionMultiplexer>();

    /// <summary>
    /// Writes a value through <see cref="ICacheService.SetAsync"/> then inspects the raw
    /// Redis keyspace via <c>KeyExistsAsync</c> to confirm the effective key format is
    /// <c>{KeyPrefix}v2:{user-key}</c>.
    ///
    /// The test fails if the expected key is not found, verifying that the prefix wiring is
    /// not silently dropped.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithKeyPrefix_RawRedisKeyContainsPrefix()
    {
        const string value = "prefix-verified";
        await Cache.SetAsync(UserKey, value, CachePolicy.Default);

        // Allow FusionCache async L2 write to propagate.
        await Task.Delay(500);

        var db = Multiplexer.GetDatabase();

        // Effective key: {KeyPrefix}v2:{user-key}
        var expectedKey = $"{KeyPrefix}{FusionCacheWireFormatSegment}{UserKey}";
        var keyExists = await db.KeyExistsAsync(expectedKey);

        Assert.True(keyExists,
            $"Expected to find Redis key '{expectedKey}' but it was not present. " +
            $"This means the KeyPrefix '{KeyPrefix}' is not being applied to L2 entries, " +
            $"or the key format has changed from the expected '{KeyPrefix}{FusionCacheWireFormatSegment}{{user-key}}'.");
        Assert.Equal(RedisType.String, await db.KeyTypeAsync(expectedKey));
        Assert.False(await db.KeyExistsAsync(FusionCacheWireFormatSegment + UserKey), "The unprefixed key must not be written.");
    }

    /// <summary>
    /// Verifies that the value stored under the prefixed key is readable via
    /// <see cref="ICacheService.TryGetAsync"/> — confirming the round-trip is not broken
    /// by the prefix wiring.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithKeyPrefix_TryGetAsync_ReturnsCorrectValue()
    {
        const string key = "prefix-test:roundtrip:1";
        const string value = "round-trip-value";

        await Cache.SetAsync(key, value, CachePolicy.Default);
        var result = await Cache.TryGetAsync<string>(key);

        Assert.Equal(value, result.Value);
    }

    /// <summary>
    /// Enumerates keys matching the prefix pattern to confirm the prefix is applied and
    /// not doubled (e.g., no <c>myservicemyservice...</c> pattern).
    /// The effective key pattern is <c>{KeyPrefix}v2:*</c>.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithKeyPrefix_KeysInRedis_MatchSinglePrefixPattern()
    {
        const string key = "prefix-test:keys:1";
        const string value = "keys-check-value";

        await Cache.SetAsync(key, value, CachePolicy.Default);
        await Task.Delay(500);

        var server = Multiplexer.GetServer(_redisContainer.GetConnectionString());

        // All keys written by this instance should start with {KeyPrefix}v2:.
        var prefixedKeys = server.Keys(pattern: $"{KeyPrefix}{FusionCacheWireFormatSegment}*")
            .Select(k => k.ToString())
            .ToList();

        Assert.NotEmpty(prefixedKeys);

        // No key should start with the prefix doubled (e.g. "myservicemyservice...").
        var doubledPrefix = $"{KeyPrefix}{KeyPrefix}";
        Assert.DoesNotContain(prefixedKeys, k => k.StartsWith(doubledPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that when <c>KeyPrefix</c> is empty (default), entries are stored under
    /// <c>v2:{user-key}</c> without any additional prefix.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithoutKeyPrefix_RawRedisKeyHasNoExtraPrefix()
    {
        // Spin up a separate container + provider for the no-prefix scenario.
        await using var container = new RedisBuilder("redis:7-alpine").Build();

        await container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = container.GetConnectionString());
        services.AddSharedKernelCaching(o => o.ServiceName = "noprefix-test-svc")
                .AddRedisL2();  // no KeyPrefix

        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ICacheService>();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();

        const string userKey = "noprefix:entry:1";
        await cache.SetAsync(userKey, "no-prefix-value", CachePolicy.Default);
        await Task.Delay(500);

        var db = multiplexer.GetDatabase();

        // Without a KeyPrefix the effective key is just v2:{user-key}.
        var expectedKey = $"{FusionCacheWireFormatSegment}{userKey}";
        var keyExists = await db.KeyExistsAsync(expectedKey);

        Assert.True(keyExists,
            $"Expected to find Redis key '{expectedKey}' but it was not present. " +
            $"The L2 key format may have changed.");
    }
}
