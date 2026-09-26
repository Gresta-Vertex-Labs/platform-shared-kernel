using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeTenantCacheService"/>'s tenant isolation, tenant tag scoping and key format,
/// and <see cref="CachingServiceCollectionExtensions.AddFakeTenantCacheService"/>'s registration shape.
/// </summary>
public sealed class FakeTenantCacheServiceTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    private static readonly TenantId TenantB = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

    [Fact]
    public async Task TryGetAsync_DifferentTenants_SameEntityAndId_NeverCollide()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "42", 100, CachePolicy.Default);

        var tenantA = await cache.TryGetAsync<int>(TenantA, "invoice", "42");
        var tenantB = await cache.TryGetAsync<int>(TenantB, "invoice", "42");

        Assert.Equal(CacheLookup<int>.Hit(100), tenantA);
        Assert.False(tenantB.IsHit);
    }

    [Fact]
    public async Task SetAsync_StoresUnderRealTenantKeyFormat()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "42", 100, CachePolicy.Default);

        var expectedKey = CacheKeyFormat.BuildTenantKey("test-svc", TenantA, "invoice", "42");
        Assert.Equal(CacheLookup<int>.Hit(100), await cache.Cache.TryGetAsync<int>(expectedKey));
        Assert.Equal(1, cache.Cache.Count);
    }

    [Fact]
    public async Task SetAsync_ScopesPolicyTagsToTenant()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "order", "1", "v", CachePolicy.Default.WithTags("orders"));

        var key = CacheKeyFormat.BuildTenantKey("test-svc", TenantA, "order", "1");
        Assert.Equal(
            [CacheKeyFormat.BuildTenantTag(TenantA, "orders"), CacheKeyFormat.BuildTenantWideTag(TenantA)],
            cache.Cache.GetTags(key));
    }

    [Fact]
    public async Task RemoveByTagAsync_SameTagNameDifferentTenants_OnlyRemovesCallingTenantsEntries()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "1", 1, CachePolicy.Default.WithTags("shared-tag"));
        await cache.SetAsync(TenantA, "invoice", "2", 2, CachePolicy.Default.WithTags("shared-tag"));
        await cache.SetAsync(TenantB, "invoice", "1", 99, CachePolicy.Default.WithTags("shared-tag"));

        await cache.RemoveByTagAsync(TenantA, "shared-tag");

        Assert.False((await cache.TryGetAsync<int>(TenantA, "invoice", "1")).IsHit);
        Assert.False((await cache.TryGetAsync<int>(TenantA, "invoice", "2")).IsHit);
        Assert.Equal(CacheLookup<int>.Hit(99), await cache.TryGetAsync<int>(TenantB, "invoice", "1"));
    }

    [Fact]
    public async Task RemoveTenantAsync_RemovesEveryEntryOfThatTenantOnly()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync(TenantA, "order", "2", 2, CachePolicy.Default.WithTags("orders"));
        await cache.SetAsync(TenantB, "invoice", "1", 3, CachePolicy.Default);

        await cache.RemoveTenantAsync(TenantA);

        Assert.False((await cache.TryGetAsync<int>(TenantA, "invoice", "1")).IsHit);
        Assert.False((await cache.TryGetAsync<int>(TenantA, "order", "2")).IsHit);
        Assert.Equal(CacheLookup<int>.Hit(3), await cache.TryGetAsync<int>(TenantB, "invoice", "1"));
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

        var first = await cache.GetOrSetAsync(TenantA, "invoice", "1", Factory, CachePolicy.Default);
        var second = await cache.GetOrSetAsync(TenantA, "invoice", "1", Factory, CachePolicy.Default);

        Assert.Equal(7, first);
        Assert.Equal(7, second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrSetAsync_ContextOverload_SkipCaching_NotStored()
    {
        var cache = new FakeTenantCacheService();

        var value = await cache.GetOrSetAsync(
            TenantA,
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

        var tenantAValue = await cache.GetOrSetAsync(TenantA, "invoice", "1", Factory, CachePolicy.Default);
        var tenantBValue = await cache.GetOrSetAsync(TenantB, "invoice", "1", Factory, CachePolicy.Default);

        Assert.Equal(1, tenantAValue);
        Assert.Equal(2, tenantBValue);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task RemoveAsync_IsTenantScoped_OtherTenantsIdenticalKeySurvives()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "1", "a", CachePolicy.Default);
        await cache.SetAsync(TenantB, "invoice", "1", "b", CachePolicy.Default);

        await cache.RemoveAsync(TenantA, "invoice", "1");

        Assert.False((await cache.TryGetAsync<string>(TenantA, "invoice", "1")).IsHit);
        Assert.Equal(CacheLookup<string>.Hit("b"), await cache.TryGetAsync<string>(TenantB, "invoice", "1"));
    }

    [Fact]
    public async Task ExpireAsync_IsTenantScoped()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync(TenantB, "invoice", "1", 2, CachePolicy.Default);

        await cache.ExpireAsync(TenantA, "invoice", "1");

        Assert.False((await cache.TryGetAsync<int>(TenantA, "invoice", "1")).IsHit);
        Assert.True((await cache.TryGetAsync<int>(TenantB, "invoice", "1")).IsHit);
    }

    [Fact]
    public async Task Reset_ClearsEveryTenantsData()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync(TenantB, "invoice", "1", 2, CachePolicy.Default);

        cache.Reset();

        Assert.Equal(0, cache.Count);
        Assert.False((await cache.TryGetAsync<int>(TenantA, "invoice", "1")).IsHit);
        Assert.False((await cache.TryGetAsync<int>(TenantB, "invoice", "1")).IsHit);
    }

    [Fact]
    public async Task SetAsync_ReSetWithFewerTags_RetiresPriorTagAssociation()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync(TenantA, "invoice", "1", 1, CachePolicy.Default.WithTags("tag-x"));
        // Re-set the same key with no tags — the prior "tag-x" association must be retired.
        await cache.SetAsync(TenantA, "invoice", "1", 2, CachePolicy.Default);

        await cache.RemoveByTagAsync(TenantA, "tag-x");

        Assert.Equal(CacheLookup<int>.Hit(2), await cache.TryGetAsync<int>(TenantA, "invoice", "1"));
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
