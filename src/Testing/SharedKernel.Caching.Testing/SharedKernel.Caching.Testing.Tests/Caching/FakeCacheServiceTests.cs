using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeCacheService"/> against the <see cref="ICacheService"/> contract: hit versus
/// miss, the factory's skip-caching decision, tags, and the batch and clear operations.
/// </summary>
public sealed class FakeCacheServiceTests
{
    [Fact]
    public async Task TryGetAsync_MissingKey_IsMiss()
    {
        var cache = new FakeCacheService();

        var lookup = await cache.TryGetAsync<int>("absent");

        Assert.False(lookup.IsHit);
    }

    [Fact]
    public async Task TryGetAsync_StoredValue_IsHit()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 42, CachePolicy.Default);

        var lookup = await cache.TryGetAsync<int>("k");

        Assert.True(lookup.IsHit);
        Assert.Equal(42, lookup.Value);
    }

    [Fact]
    public async Task TryGetAsync_CachedZero_IsHitNotMiss()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 0, CachePolicy.Default);

        var lookup = await cache.TryGetAsync<int>("k");

        Assert.True(lookup.IsHit);
        Assert.Equal(0, lookup.Value);
    }

    [Fact]
    public async Task TryGetAsync_CachedNull_IsHitNotMiss()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync<string?>("k", null, CachePolicy.Default);

        var lookup = await cache.TryGetAsync<string?>("k");

        Assert.True(lookup.IsHit);
        Assert.Null(lookup.Value);
    }

    [Fact]
    public async Task GetOrSetAsync_CachedNull_DoesNotRunFactoryAgain()
    {
        var cache = new FakeCacheService();

        await cache.GetOrSetAsync<string?>("k", _ => ValueTask.FromResult<string?>(null), CachePolicy.Default);
        var second = await cache.GetOrSetAsync<string?>("k", _ => ValueTask.FromResult<string?>("recomputed"), CachePolicy.Default);

        Assert.Null(second);
        Assert.Equal(1, cache.FactoryInvocationCount);
    }

    [Fact]
    public async Task GetOrSetAsync_CallsFactoryOnlyOnMiss()
    {
        var cache = new FakeCacheService();
        var calls = 0;

        ValueTask<int> Factory(CancellationToken ct)
        {
            calls++;
            return ValueTask.FromResult(42);
        }

        var first = await cache.GetOrSetAsync("k", Factory, CachePolicy.Default);
        var second = await cache.GetOrSetAsync("k", Factory, CachePolicy.Default);

        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.FactoryInvocationCount);
    }

    [Fact]
    public async Task GetOrSetAsync_FactorySkipsCaching_ValueReturnedButNotStored()
    {
        var cache = new FakeCacheService();

        var value = await cache.GetOrSetAsync(
            "k",
            (context, _) =>
            {
                context.SkipCaching();
                return ValueTask.FromResult(7);
            },
            CachePolicy.Default);

        Assert.Equal(7, value);
        Assert.False((await cache.TryGetAsync<int>("k")).IsHit);
        Assert.Equal(0, cache.Count);

        await cache.GetOrSetAsync("k", _ => ValueTask.FromResult(8), CachePolicy.Default);
        Assert.Equal(2, cache.FactoryInvocationCount);
    }

    [Fact]
    public async Task GetOrSetAsync_ContextCarriesKeyAndPolicy()
    {
        var cache = new FakeCacheService();
        var policy = CachePolicy.For(TimeSpan.FromMinutes(1)).WithTags("t");
        CacheFactoryContext? seen = null;

        await cache.GetOrSetAsync(
            "k",
            (context, _) =>
            {
                seen = context;
                return ValueTask.FromResult(1);
            },
            policy);

        Assert.NotNull(seen);
        Assert.Equal("k", seen.Key);
        Assert.Same(policy, seen.Policy);
    }

    [Fact]
    public async Task SetAsync_StoresPolicyTags_ReadableThroughGetTags()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 1, CachePolicy.Default.WithTags("a", "b"));

        Assert.Equal(["a", "b"], cache.GetTags("k"));
        Assert.Null(cache.GetTags("absent"));
    }

    [Fact]
    public async Task RemoveByTagAsync_RemovesOnlyTaggedEntries()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("tagged", 1, CachePolicy.Default.WithTags("t"));
        await cache.SetAsync("untagged", 2, CachePolicy.Default);

        await cache.RemoveByTagAsync("t");

        Assert.False((await cache.TryGetAsync<int>("tagged")).IsHit);
        Assert.True((await cache.TryGetAsync<int>("untagged")).IsHit);
    }

    [Fact]
    public async Task RemoveByTagsAsync_RemovesEntriesMatchingAnyTag()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("a", 1, CachePolicy.Default.WithTags("x"));
        await cache.SetAsync("b", 2, CachePolicy.Default.WithTags("y"));
        await cache.SetAsync("c", 3, CachePolicy.Default.WithTags("z"));

        await cache.RemoveByTagsAsync(["x", "y"]);

        Assert.False((await cache.TryGetAsync<int>("a")).IsHit);
        Assert.False((await cache.TryGetAsync<int>("b")).IsHit);
        Assert.True((await cache.TryGetAsync<int>("c")).IsHit);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntryAndTags()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 1, CachePolicy.Default.WithTags("t"));

        await cache.RemoveAsync("k");

        Assert.False((await cache.TryGetAsync<int>("k")).IsHit);
        Assert.Null(cache.GetTags("k"));
    }

    [Fact]
    public async Task ExpireAsync_RemovesEntry()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("k", 1, CachePolicy.Default);

        await cache.ExpireAsync("k");

        Assert.False((await cache.TryGetAsync<int>("k")).IsHit);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("a", 1, CachePolicy.Default);
        await cache.SetAsync("b", 2, CachePolicy.Default.WithTags("t"));

        await cache.ClearAsync();

        Assert.Equal(0, cache.Count);
        Assert.False((await cache.TryGetAsync<int>("a")).IsHit);
    }

    [Fact]
    public async Task Clear_RemovesAllEntriesAndResetsFactoryInvocationCount()
    {
        var cache = new FakeCacheService();
        await cache.GetOrSetAsync("k", _ => ValueTask.FromResult(1), CachePolicy.Default);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.FactoryInvocationCount);
    }

    [Fact]
    public async Task TryGetManyAsync_ReportsHitsAndMissesPerKey()
    {
        var cache = new FakeCacheService();
        await cache.SetAsync("a", 1, CachePolicy.Default);
        await cache.SetAsync("zero", 0, CachePolicy.Default);

        var result = await cache.TryGetManyAsync<int>(["a", "zero", "b"]);

        Assert.Equal(3, result.Count);
        Assert.Equal(CacheLookup<int>.Hit(1), result["a"]);
        Assert.Equal(CacheLookup<int>.Hit(0), result["zero"]);
        Assert.False(result["b"].IsHit);
    }

    [Fact]
    public async Task TryGetManyAsync_EmptyKeys_ReturnsEmptyDictionary()
    {
        var cache = new FakeCacheService();

        var result = await cache.TryGetManyAsync<int>([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SetManyAsync_AppliesSamePolicyToAllEntries()
    {
        var cache = new FakeCacheService();
        var policy = CachePolicy.Default.WithTags("tag-a");

        await cache.SetManyAsync(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, policy);
        Assert.Equal(2, cache.Count);

        await cache.RemoveByTagAsync("tag-a");

        var result = await cache.TryGetManyAsync<int>(["a", "b"]);
        Assert.False(result["a"].IsHit);
        Assert.False(result["b"].IsHit);
    }

    [Fact]
    public async Task SetManyAsync_EmptyEntries_IsNoOp()
    {
        var cache = new FakeCacheService();
        await cache.SetManyAsync(new Dictionary<string, int>(), CachePolicy.Default);

        Assert.Equal(0, cache.Count);
    }
}
