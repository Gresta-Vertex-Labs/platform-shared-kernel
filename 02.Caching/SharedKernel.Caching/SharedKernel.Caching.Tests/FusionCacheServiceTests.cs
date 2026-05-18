using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Extensions;
using SharedKernel.Caching.Implementations;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.Tests;

/// <summary>
/// Unit tests for <see cref="FusionCacheService"/> using an in-process FusionCache instance.
/// Covers get, set, remove, tag-eviction, and stampede protection.
/// </summary>
public sealed class FusionCacheServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;

    public FusionCacheServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching();
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -------------------------------------------------------------------------
    // GetAsync / SetAsync round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsStoredValue()
    {
        const string key = "test:set-get";
        const string value = "hello-world";

        await _cache.SetAsync(key, value, CachePolicy.Default);
        var result = await _cache.GetAsync<string>(key);

        Assert.Equal(value, result);
    }

    [Fact]
    public async Task GetAsync_UnknownKey_ReturnsNull()
    {
        var result = await _cache.GetAsync<string>("test:nonexistent-" + Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_NullOrWhitespaceKey_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _cache.GetAsync<string>("").AsTask());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _cache.GetAsync<string>("   ").AsTask());
    }

    // -------------------------------------------------------------------------
    // RemoveAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_ExistingKey_ReturnsNullAfterRemoval()
    {
        const string key = "test:remove";
        await _cache.SetAsync(key, 42, CachePolicy.Default);

        await _cache.RemoveAsync(key);

        var result = await _cache.GetAsync<int?>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        // No-op — must not throw.
        var ex = await Record.ExceptionAsync(() =>
            _cache.RemoveAsync("test:does-not-exist-" + Guid.NewGuid()).AsTask());

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // GetOrSetAsync — value and stampede protection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_InvokesFactoryAndCachesResult()
    {
        var key = "test:get-or-set-" + Guid.NewGuid();
        var factoryCalls = 0;

        var result = await _cache.GetOrSetAsync(
            key,
            async ct => { factoryCalls++; return "factory-value"; },
            CachePolicy.Default);

        Assert.Equal("factory-value", result);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_DoesNotInvokeFactory()
    {
        var key = "test:get-or-set-hit-" + Guid.NewGuid();
        await _cache.SetAsync(key, "cached", CachePolicy.Default);

        var factoryCalls = 0;
        var result = await _cache.GetOrSetAsync(
            key,
            async ct => { factoryCalls++; return "fresh"; },
            CachePolicy.Default);

        Assert.Equal("cached", result);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_StampedeProtection_FactoryCalledExactlyOnce()
    {
        var key = "test:stampede-" + Guid.NewGuid();
        var factoryCalls = 0;

        // Fire 20 concurrent requests for the same uncached key.
        const int concurrency = 20;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            _cache.GetOrSetAsync(
                key,
                async ct =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(50, ct); // simulate work
                    return "stampede-value";
                },
                CachePolicy.Default).AsTask());

        var results = await Task.WhenAll(tasks);

        // All results must be the same value.
        Assert.All(results, r => Assert.Equal("stampede-value", r));

        // The factory must have been invoked exactly once thanks to FusionCache stampede protection.
        Assert.Equal(1, factoryCalls);
    }

    // -------------------------------------------------------------------------
    // Tag-based eviction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByTagAsync_EvictsAllEntriesWithTag()
    {
        const string tag = "test-tag-eviction";
        var policy = CachePolicy.Default.WithTags(tag);

        await _cache.SetAsync("test:tagged:1", "a", policy);
        await _cache.SetAsync("test:tagged:2", "b", policy);
        await _cache.SetAsync("test:untagged:1", "c", CachePolicy.Default);

        await _cache.RemoveByTagAsync(tag);

        // Tagged entries should be gone.
        Assert.Null(await _cache.GetAsync<string>("test:tagged:1"));
        Assert.Null(await _cache.GetAsync<string>("test:tagged:2"));

        // Untagged entry must still be present.
        Assert.Equal("c", await _cache.GetAsync<string>("test:untagged:1"));
    }

    [Fact]
    public async Task RemoveByTagAsync_NullOrWhitespaceTag_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _cache.RemoveByTagAsync("").AsTask());
    }
}
