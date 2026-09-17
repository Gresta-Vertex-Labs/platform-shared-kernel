using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeTenantCacheService"/>'s tenant isolation, tenant tag scoping and key format,
/// and <see cref="CachingServiceCollectionExtensions.AddFakeTenantCacheService"/>'s registration shape.
/// </summary>
public sealed class FakeTenantCacheServiceTests
{
    [Fact]
    public async Task TryGetAsync_DifferentTenants_SameEntityAndId_NeverCollide()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "42", 100, CachePolicy.Default);

        var tenantA = await cache.TryGetAsync<int>("tenant-a", "invoice", "42");
        var tenantB = await cache.TryGetAsync<int>("tenant-b", "invoice", "42");

        Assert.Equal(CacheLookup<int>.Hit(100), tenantA);
        Assert.False(tenantB.IsHit);
    }

    [Fact]
    public async Task SetAsync_StoresUnderRealTenantKeyFormat()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "42", 100, CachePolicy.Default);

        var expectedKey = CacheKeyFormat.BuildTenantKey("test-svc", "tenant-a", "invoice", "42");
        Assert.Equal(CacheLookup<int>.Hit(100), await cache.Cache.TryGetAsync<int>(expectedKey));
        Assert.Equal(1, cache.Cache.Count);
    }

    [Fact]
    public async Task SetAsync_ScopesPolicyTagsToTenant()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "order", "1", "v", CachePolicy.Default.WithTags("orders"));

        var key = CacheKeyFormat.BuildTenantKey("test-svc", "tenant-a", "order", "1");
        Assert.Equal(
            [CacheKeyFormat.BuildTenantTag("tenant-a", "orders"), CacheKeyFormat.BuildTenantWideTag("tenant-a")],
            cache.Cache.GetTags(key));
    }

    [Fact]
    public async Task RemoveByTagAsync_SameTagNameDifferentTenants_OnlyRemovesCallingTenantsEntries()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default.WithTags("shared-tag"));
        await cache.SetAsync("tenant-a", "invoice", "2", 2, CachePolicy.Default.WithTags("shared-tag"));
        await cache.SetAsync("tenant-b", "invoice", "1", 99, CachePolicy.Default.WithTags("shared-tag"));

        await cache.RemoveByTagAsync("tenant-a", "shared-tag");

        Assert.False((await cache.TryGetAsync<int>("tenant-a", "invoice", "1")).IsHit);
        Assert.False((await cache.TryGetAsync<int>("tenant-a", "invoice", "2")).IsHit);
        Assert.Equal(CacheLookup<int>.Hit(99), await cache.TryGetAsync<int>("tenant-b", "invoice", "1"));
    }

    [Fact]
    public async Task RemoveTenantAsync_RemovesEveryEntryOfThatTenantOnly()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync("tenant-a", "order", "2", 2, CachePolicy.Default.WithTags("orders"));
        await cache.SetAsync("tenant-b", "invoice", "1", 3, CachePolicy.Default);

        await cache.RemoveTenantAsync("tenant-a");

        Assert.False((await cache.TryGetAsync<int>("tenant-a", "invoice", "1")).IsHit);
        Assert.False((await cache.TryGetAsync<int>("tenant-a", "order", "2")).IsHit);
        Assert.Equal(CacheLookup<int>.Hit(3), await cache.TryGetAsync<int>("tenant-b", "invoice", "1"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task GetOrSetAsync_OnMiss_InvokesFactoryOnce_ThenServesCachedValueOnSubsequentHit()
    {
        var cache = new FakeTenantCacheService();
        var callCount = 0;

        ValueTask<int> Factory(CancellationToken ct)
        {
            callCount++;
            return ValueTask.FromResult(7);
        }

        var first = await cache.GetOrSetAsync("tenant-a", "invoice", "1", Factory, CachePolicy.Default);
        var second = await cache.GetOrSetAsync("tenant-a", "invoice", "1", Factory, CachePolicy.Default);

        Assert.Equal(7, first);
        Assert.Equal(7, second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrSetAsync_ContextOverload_SkipCaching_NotStored()
    {
        var cache = new FakeTenantCacheService();

        var value = await cache.GetOrSetAsync(
            "tenant-a",
            "invoice",
            "1",
            (context, _) =>
            {
                context.SkipCaching();
                return ValueTask.FromResult(5);
            },
            CachePolicy.Default);

        Assert.Equal(5, value);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task GetOrSetAsync_DifferentTenants_SameEntityAndId_EachInvokesFactoryIndependently()
    {
        var cache = new FakeTenantCacheService();
        var callCount = 0;

        ValueTask<int> Factory(CancellationToken ct)
        {
            callCount++;
            return ValueTask.FromResult(callCount);
        }

        var tenantAValue = await cache.GetOrSetAsync("tenant-a", "invoice", "1", Factory, CachePolicy.Default);
        var tenantBValue = await cache.GetOrSetAsync("tenant-b", "invoice", "1", Factory, CachePolicy.Default);

        Assert.Equal(1, tenantAValue);
        Assert.Equal(2, tenantBValue);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task RemoveAsync_IsTenantScoped_OtherTenantsIdenticalKeySurvives()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", "a", CachePolicy.Default);
        await cache.SetAsync("tenant-b", "invoice", "1", "b", CachePolicy.Default);

        await cache.RemoveAsync("tenant-a", "invoice", "1");

        Assert.False((await cache.TryGetAsync<string>("tenant-a", "invoice", "1")).IsHit);
        Assert.Equal(CacheLookup<string>.Hit("b"), await cache.TryGetAsync<string>("tenant-b", "invoice", "1"));
    }

    [Fact]
    public async Task ExpireAsync_IsTenantScoped()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync("tenant-b", "invoice", "1", 2, CachePolicy.Default);

        await cache.ExpireAsync("tenant-a", "invoice", "1");

        Assert.False((await cache.TryGetAsync<int>("tenant-a", "invoice", "1")).IsHit);
        Assert.True((await cache.TryGetAsync<int>("tenant-b", "invoice", "1")).IsHit);
    }

    [Fact]
    public async Task Reset_ClearsEveryTenantsData()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync("tenant-b", "invoice", "1", 2, CachePolicy.Default);

        cache.Reset();

        Assert.Equal(0, cache.Count);
        Assert.False((await cache.TryGetAsync<int>("tenant-a", "invoice", "1")).IsHit);
        Assert.False((await cache.TryGetAsync<int>("tenant-b", "invoice", "1")).IsHit);
    }

    [Fact]
    public async Task SetAsync_ReSetWithFewerTags_RetiresPriorTagAssociation()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default.WithTags("tag-x"));
        // Re-set the same key with no tags — the prior "tag-x" association must be retired.
        await cache.SetAsync("tenant-a", "invoice", "1", 2, CachePolicy.Default);

        await cache.RemoveByTagAsync("tenant-a", "tag-x");

        Assert.Equal(CacheLookup<int>.Hit(2), await cache.TryGetAsync<int>("tenant-a", "invoice", "1"));
    }

    [Fact]
    public void AddFakeTenantCacheService_RegistersWorkingSingleton()
    {
        var services = new ServiceCollection();
        services.AddFakeTenantCacheService();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ITenantCacheService>();
        var second = provider.GetRequiredService<ITenantCacheService>();

        Assert.IsType<FakeTenantCacheService>(first);
        Assert.Same(first, second);
    }
}
