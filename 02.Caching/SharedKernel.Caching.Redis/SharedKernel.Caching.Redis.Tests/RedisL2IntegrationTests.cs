using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Extensions;
using SharedKernel.Caching.Policies;
using SharedKernel.Caching.Redis.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Integration tests for the Redis L2 distributed backplane.
/// Uses Testcontainers to spin up a real Redis instance.
/// </summary>
[Collection("Redis")]
public sealed class RedisL2IntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching()
                .AddRedisL2(_redisContainer.GetConnectionString());

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    private ICacheService Cache => _provider!.GetRequiredService<ICacheService>();

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsValueThroughRedis()
    {
        const string key = "redis:roundtrip:1";
        const string value = "stored-in-redis";

        await Cache.SetAsync(key, value, CachePolicy.Default);
        var result = await Cache.GetAsync<string>(key);

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetOrSetAsync_WithRedisL2_FactoryCalledOnce()
    {
        var key = "redis:get-or-set-" + Guid.NewGuid();
        var factoryCalls = 0;

        var result1 = await Cache.GetOrSetAsync(
            key,
            async _ => { factoryCalls++; return "redis-value"; },
            CachePolicy.Default);

        var result2 = await Cache.GetOrSetAsync(
            key,
            async _ => { factoryCalls++; return "should-not-be-returned"; },
            CachePolicy.Default);

        Assert.Equal("redis-value", result1);
        Assert.Equal("redis-value", result2);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromAllLayers()
    {
        var key = "redis:remove-" + Guid.NewGuid();
        await Cache.SetAsync(key, "to-remove", CachePolicy.Default);

        await Cache.RemoveAsync(key);

        var result = await Cache.GetAsync<string>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_WithTags_ThenRemoveByTag_EvictsTaggedEntries()
    {
        var tag = "redis-tag-" + Guid.NewGuid();
        var policy = CachePolicy.Default.WithTags(tag);

        await Cache.SetAsync("redis:tagged:a", "alpha", policy);
        await Cache.SetAsync("redis:tagged:b", "beta", policy);

        await Cache.RemoveByTagAsync(tag);

        Assert.Null(await Cache.GetAsync<string>("redis:tagged:a"));
        Assert.Null(await Cache.GetAsync<string>("redis:tagged:b"));
    }

    [Fact]
    public async Task StampedeProtection_WithRedisL2_FactoryCalledExactlyOnce()
    {
        var key = "redis:stampede-" + Guid.NewGuid();
        var factoryCalls = 0;

        const int concurrency = 10;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            Cache.GetOrSetAsync(
                key,
                async ct =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(50, ct);
                    return "stampede-protected";
                },
                CachePolicy.Default).AsTask());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("stampede-protected", r));
        Assert.Equal(1, factoryCalls);
    }
}
