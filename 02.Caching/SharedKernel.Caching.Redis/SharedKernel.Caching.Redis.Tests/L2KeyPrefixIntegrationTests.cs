using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
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
/// Verified effective L2 key format:
/// <c>{KeyPrefix}v2:{user-key}</c>
/// </para>
/// <para>
/// The <c>v2:</c> segment is a schema-version marker prepended by
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> (v10+) after the
/// <c>InstanceName</c> (= <c>KeyPrefix</c>). It is not added by FusionCache or by
/// the SharedKernel caching layer.
/// </para>
/// </summary>
[Collection("Redis")]
public sealed class L2KeyPrefixIntegrationTests : IAsyncLifetime
{
    private const string KeyPrefix = "myservice";
    private const string UserKey = "prefix-test:entry:1";

    /// <summary>
    /// The schema-version separator inserted by
    /// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> v10+
    /// between the <c>InstanceName</c> (KeyPrefix) and the user key.
    /// </summary>
    private const string RedisSchemaVersionSeparator = "v2:";

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "prefix-test-svc")
                .AddRedisL2(_redisContainer.GetConnectionString(), o =>
                {
                    o.KeyPrefix = KeyPrefix;
                });

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
    /// <para>
    /// The <c>v2:</c> schema-version separator is appended by
    /// <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> v10+ after the
    /// <c>InstanceName</c>. This is an implementation detail of that library — SharedKernel
    /// does not add it.
    /// </para>
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
        // The "v2:" schema marker is prepended by Microsoft.Extensions.Caching.StackExchangeRedis v10+.
        var expectedKey = $"{KeyPrefix}{RedisSchemaVersionSeparator}{UserKey}";
        var keyExists = await db.KeyExistsAsync(expectedKey);

        Assert.True(keyExists,
            $"Expected to find Redis key '{expectedKey}' but it was not present. " +
            $"This means the KeyPrefix '{KeyPrefix}' is not being applied to L2 entries, " +
            $"or the key format has changed from the expected '{KeyPrefix}{RedisSchemaVersionSeparator}{{user-key}}'.");
    }

    /// <summary>
    /// Verifies that the value stored under the prefixed key is readable via
    /// <see cref="ICacheService.GetAsync"/> — confirming the round-trip is not broken
    /// by the prefix wiring.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithKeyPrefix_GetAsync_ReturnsCorrectValue()
    {
        const string key = "prefix-test:roundtrip:1";
        const string value = "round-trip-value";

        await Cache.SetAsync(key, value, CachePolicy.Default);
        var result = await Cache.GetAsync<string>(key);

        Assert.Equal(value, result);
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
        var prefixedKeys = server.Keys(pattern: $"{KeyPrefix}{RedisSchemaVersionSeparator}*")
            .Select(k => k.ToString())
            .ToList();

        Assert.NotEmpty(prefixedKeys);

        // No key should start with the prefix doubled (e.g. "myservicemyservice...").
        var doubledPrefix = $"{KeyPrefix}{KeyPrefix}";
        Assert.DoesNotContain(prefixedKeys, k => k.StartsWith(doubledPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that when <c>KeyPrefix</c> is empty (default), entries are stored under
    /// the schema-version key format <c>v2:{user-key}</c> without any additional prefix.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithoutKeyPrefix_RawRedisKeyHasNoExtraPrefix()
    {
        // Spin up a separate container + provider for the no-prefix scenario.
        await using var container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "noprefix-test-svc")
                .AddRedisL2(container.GetConnectionString());  // no KeyPrefix

        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ICacheService>();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();

        const string userKey = "noprefix:entry:1";
        await cache.SetAsync(userKey, "no-prefix-value", CachePolicy.Default);
        await Task.Delay(500);

        var db = multiplexer.GetDatabase();

        // Without a KeyPrefix the effective key is just v2:{user-key}.
        var expectedKey = $"{RedisSchemaVersionSeparator}{userKey}";
        var keyExists = await db.KeyExistsAsync(expectedKey);

        Assert.True(keyExists,
            $"Expected to find Redis key '{expectedKey}' but it was not present. " +
            $"Redis schema-version key format may have changed.");
    }
}
