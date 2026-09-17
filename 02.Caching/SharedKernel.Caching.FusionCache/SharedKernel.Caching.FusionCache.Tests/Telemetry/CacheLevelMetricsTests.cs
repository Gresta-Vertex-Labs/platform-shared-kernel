using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.FusionCache.Tests.Telemetry;

/// <summary>
/// <c>cache.hits</c> carries the layer that answered (<c>l1</c> or <c>l2</c>), and <c>cache.misses</c>
/// counts only reads no layer could answer: a <c>TryGet</c>/<c>TryGetMany</c> miss or a factory run.
/// </summary>
/// <remarks>
/// The distributed layer is an in-memory <see cref="MemoryDistributedCache"/> shared by two independent
/// providers ("nodes"), each with its own memory cache, so a value written by one node is a distributed
/// hit on the other. Every test uses a fresh entity name, so its <c>cache.key_prefix</c> series is its own.
/// </remarks>
public sealed class CacheLevelMetricsTests
{
    private readonly IDistributedCache _sharedDistributedCache =
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private readonly string _entity = "e" + Guid.NewGuid().ToString("N");

    private string Prefix => "svc:" + _entity;

    private string Key(string id) => CacheKeyFormat.BuildKey("svc", _entity, id);

    [Fact]
    public async Task DistributedHit_OnAnotherNode_IsCountedAsL2Hit_AndNotAsAMiss()
    {
        await using var nodeA = BuildNode(_sharedDistributedCache);
        await using var nodeB = BuildNode(_sharedDistributedCache);
        var key = Key("1");

        await nodeA.GetRequiredService<ICacheService>().SetAsync(key, "from-a", CachePolicy.Default);
        var cacheB = nodeB.GetRequiredService<ICacheService>();

        using var metrics = new MetricRecorder();

        var lookup = await cacheB.TryGetAsync<string>(key);

        Assert.Equal("from-a", lookup.Value);
        Assert.True(await Eventually.HoldsAsync(() => metrics.Sum("cache.hits", Prefix, "l2") == 1),
            $"Expected one l2 hit; saw {Describe(metrics)}");
        await Task.Delay(100); // let any stray background event land before asserting absence
        Assert.Equal(0, metrics.Sum("cache.hits", Prefix, "l1"));
        Assert.Equal(0, metrics.Sum("cache.misses", Prefix));
    }

    [Fact]
    public async Task GetOrSet_AnsweredByDistributedLayer_IsAnL2Hit_WithNoMissAndNoFactoryRun()
    {
        await using var nodeA = BuildNode(_sharedDistributedCache);
        await using var nodeB = BuildNode(_sharedDistributedCache);
        var key = Key("2");

        await nodeA.GetRequiredService<ICacheService>().SetAsync(key, "from-a", CachePolicy.Default);
        var cacheB = nodeB.GetRequiredService<ICacheService>();

        using var metrics = new MetricRecorder();

        var value = await cacheB.GetOrSetAsync<string>(
            key,
            _ => throw new InvalidOperationException("the factory must not run on a distributed hit"),
            CachePolicy.Default);

        Assert.Equal("from-a", value);
        Assert.True(await Eventually.HoldsAsync(() => metrics.Sum("cache.hits", Prefix, "l2") == 1),
            $"Expected one l2 hit; saw {Describe(metrics)}");
        Assert.Equal(0, metrics.Sum("cache.misses", Prefix));
    }

    [Fact]
    public async Task SecondReadOnTheSameNode_IsAnL1Hit()
    {
        await using var nodeA = BuildNode(_sharedDistributedCache);
        await using var nodeB = BuildNode(_sharedDistributedCache);
        var key = Key("3");

        await nodeA.GetRequiredService<ICacheService>().SetAsync(key, "from-a", CachePolicy.Default);
        var cacheB = nodeB.GetRequiredService<ICacheService>();
        await cacheB.TryGetAsync<string>(key); // copies the entry into B's memory cache

        using var metrics = new MetricRecorder();

        await cacheB.TryGetAsync<string>(key);

        Assert.True(await Eventually.HoldsAsync(() => metrics.Sum("cache.hits", Prefix, "l1") == 1),
            $"Expected one l1 hit; saw {Describe(metrics)}");
        await Task.Delay(100);
        Assert.Equal(0, metrics.Sum("cache.hits", Prefix, "l2"));
        Assert.Equal(0, metrics.Sum("cache.misses", Prefix));
    }

    [Fact]
    public async Task TryGet_MissOnEveryLayer_CountsExactlyOneMiss_AndNoHit()
    {
        await using var node = BuildNode(_sharedDistributedCache);

        using var metrics = new MetricRecorder();

        var lookup = await node.GetRequiredService<ICacheService>().TryGetAsync<string>(Key("absent"));

        Assert.False(lookup.IsHit);
        Assert.Equal(1, metrics.Sum("cache.misses", Prefix));
        await Task.Delay(100);
        Assert.Equal(0, metrics.Sum("cache.hits", Prefix));
    }

    [Fact]
    public async Task GetOrSet_FactoryRun_CountsExactlyOneMiss_ThenAHitCountsNone()
    {
        await using var node = BuildNode(_sharedDistributedCache);
        var cache = node.GetRequiredService<ICacheService>();
        var key = Key("factory");

        using var metrics = new MetricRecorder();

        Assert.Equal("f", await cache.GetOrSetAsync(key, _ => ValueTask.FromResult("f"), CachePolicy.Default));
        Assert.Equal(1, metrics.Sum("cache.misses", Prefix));

        Assert.Equal("f", await cache.GetOrSetAsync(key, _ => ValueTask.FromResult("other"), CachePolicy.Default));

        Assert.True(await Eventually.HoldsAsync(() => metrics.Sum("cache.hits", Prefix, "l1") == 1),
            $"Expected one l1 hit; saw {Describe(metrics)}");
        Assert.Equal(1, metrics.Sum("cache.misses", Prefix));
    }

    [Fact]
    public async Task TryGetMany_CountsOneMissPerAbsentKey_AndHitsForPresentKeys()
    {
        await using var node = BuildNode(_sharedDistributedCache);
        var cache = node.GetRequiredService<ICacheService>();
        await cache.SetAsync(Key("present"), "p", CachePolicy.Default);

        using var metrics = new MetricRecorder();

        var results = await cache.TryGetManyAsync<string>([Key("present"), Key("absent-1"), Key("absent-2")]);

        Assert.True(results[Key("present")].IsHit);
        Assert.Equal(2, metrics.Sum("cache.misses", Prefix));
        Assert.True(await Eventually.HoldsAsync(() => metrics.Sum("cache.hits", Prefix, "l1") == 1),
            $"Expected one l1 hit; saw {Describe(metrics)}");
    }

    [Fact]
    public async Task MemoryOnlyCache_GetOrSetFactoryRun_CountsExactlyOneMiss()
    {
        // Without a distributed layer the memory miss before the factory run must not be counted again.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();

        using var metrics = new MetricRecorder();

        await cache.GetOrSetAsync(Key("memory-only"), _ => ValueTask.FromResult(1), CachePolicy.Default);
        await Task.Delay(100);

        Assert.Equal(1, metrics.Sum("cache.misses", Prefix));
        Assert.Equal(0, metrics.Sum("cache.hits", Prefix));
    }

    private static ServiceProvider BuildNode(IDistributedCache distributedCache)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        services.AddFusionCache().WithDistributedCache(distributedCache);
        return services.BuildServiceProvider();
    }

    private string Describe(MetricRecorder metrics) =>
        string.Join(", ", metrics.Measurements
            .Where(m => m.Tag("cache.key_prefix") == Prefix)
            .Select(m => $"{m.Instrument}[{m.Tag("cache.level")}]={m.Value}"));
}
