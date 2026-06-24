using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeCacheService"/>'s batch-operation contract (<c>GetManyAsync</c>/<c>SetManyAsync</c>)
/// directly. No `02.Caching` consuming-domain test currently exercises these two methods against the
/// fake itself (only indirect DI-registration usage exists in `SharedKernel.Caching.Redis.PubSub.Tests`),
/// so this self-test closes that coverage gap per the documented SelfTests fallback.
/// </summary>
public sealed class FakeCacheServiceTests
{
    [Fact]
    public async Task GetManyAsync_EveryRequestedKey_HasEntry_MissingKeysMapToDefault()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("a", 1, CachePolicy.Default);

        var result = await cache.GetManyAsync<int>(["a", "b"]);

        Assert.Equal(1, result["a"]);
        Assert.Equal(0, result["b"]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetManyAsync_EmptyKeys_ReturnsEmptyDictionary()
    {
        var cache = new FakeCacheService();

        var result = await cache.GetManyAsync<int>([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SetManyAsync_AppliesSamePolicyToAllEntries()
    {
        var cache = new FakeCacheService();
        var policy = CachePolicy.Default.WithTags("tag-a");

        await cache.SetManyAsync(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, policy);
        await cache.RemoveByTagAsync("tag-a");

        var result = await cache.GetManyAsync<int>(["a", "b"]);
        Assert.Equal(0, result["a"]);
        Assert.Equal(0, result["b"]);
    }

    [Fact]
    public async Task SetManyAsync_EmptyEntries_IsNoOp()
    {
        var cache = new FakeCacheService();
        await cache.SetManyAsync(new Dictionary<string, int>(), CachePolicy.Default);

        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task GetOrSetAsync_CallsFactoryOnlyOnMiss()
    {
        var cache = new FakeCacheService();
        var calls = 0;

        async ValueTask<int> Factory(CancellationToken ct)
        {
            calls++;
            return 42;
        }

        await cache.GetOrSetAsync("k", Factory, CachePolicy.Default);
        await cache.GetOrSetAsync("k", Factory, CachePolicy.Default);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntryAndTags()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 1, CachePolicy.Default.WithTags("t"));

        await cache.RemoveAsync("k");

        var result = await cache.GetAsync<int>("k");
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 1, CachePolicy.Default);

        cache.Clear();

        Assert.Equal(0, cache.Count);
    }
}
