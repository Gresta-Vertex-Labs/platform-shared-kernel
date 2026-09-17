using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Verifies L1-only silent fallback behaviour when Redis is not registered
/// or when L1-only mode is used without <c>AddRedisL2</c>.
/// </summary>
public sealed class L1FallbackTests
{
    /// <summary>
    /// When <c>AddRedisL2</c> is not called, the cache must operate L1-only
    /// and return values from the in-process cache without error.
    /// </summary>
    [Fact]
    public async Task L1OnlyMode_NoRedis_CacheOperatesNormally()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc"); // No AddRedisL2

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();

        const string key = "fallback:l1-only";
        await cache.SetAsync(key, "l1-value", CachePolicy.Default);

        var result = await cache.TryGetAsync<string>(key);
        Assert.Equal("l1-value", result.Value);
    }

    /// <summary>
    /// L1-only GetOrSetAsync must still invoke factory and cache the result.
    /// </summary>
    [Fact]
    public async Task L1OnlyMode_GetOrSetAsync_WorksWithoutRedis()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();

        var factoryCalls = 0;
        var key = "fallback:get-or-set-" + Guid.NewGuid();

        var result = await cache.GetOrSetAsync(
            key,
            async _ => { factoryCalls++; return 99; },
            CachePolicy.Default);

        Assert.Equal(99, result);
        Assert.Equal(1, factoryCalls);
    }

    /// <summary>
    /// Verifies the DI registration for Redis L2 resolves ICacheService successfully.
    /// </summary>
    [Fact]
    public void AddRedisL2_DI_Registration_ResolvesICacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddRedisL2();

        using var provider = services.BuildServiceProvider();

        // ICacheService must be resolvable (connection lazily established at first use).
        var cache = provider.GetService<ICacheService>();
        Assert.NotNull(cache);
    }
}

