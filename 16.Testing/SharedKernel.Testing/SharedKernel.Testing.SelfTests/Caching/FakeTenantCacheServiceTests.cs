using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeTenantCacheService"/> per D-212's scenarios and
/// <see cref="CachingServiceCollectionExtensions.AddFakeTenantCacheService"/>'s registration shape.
/// </summary>
public sealed class FakeTenantCacheServiceTests
{
    [Fact]
    public async Task GetAsync_DifferentTenants_SameEntityAndId_NeverCollide()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "42", 100, CachePolicy.Default);

        var tenantAValue = await cache.GetAsync<int>("tenant-a", "invoice", "42");
        var tenantBValue = await cache.GetAsync<int>("tenant-b", "invoice", "42");

        Assert.Equal(100, tenantAValue);
        Assert.Equal(0, tenantBValue);
    }

    [Fact]
    public async Task RemoveByTagAsync_ScopedToTenant_OtherTenantsTaggedEntriesSurvive()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "order", "1", "a-value", CachePolicy.Default.WithTags("orders"));
        await cache.SetAsync("tenant-b", "order", "1", "b-value", CachePolicy.Default.WithTags("orders"));

        await cache.RemoveByTagAsync("tenant-a", "orders");

        var tenantAValue = await cache.GetAsync<string>("tenant-a", "order", "1");
        var tenantBValue = await cache.GetAsync<string>("tenant-b", "order", "1");

        Assert.Null(tenantAValue);
        Assert.Equal("b-value", tenantBValue);
    }

    [Fact]
    public async Task RemoveByTagAsync_SameTagNameDifferentTenants_OnlyRemovesCallingTenantsEntries()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default.WithTags("shared-tag"));
        await cache.SetAsync("tenant-a", "invoice", "2", 2, CachePolicy.Default.WithTags("shared-tag"));
        await cache.SetAsync("tenant-b", "invoice", "1", 99, CachePolicy.Default.WithTags("shared-tag"));

        await cache.RemoveByTagAsync("tenant-a", "shared-tag");

        Assert.Equal(0, await cache.GetAsync<int>("tenant-a", "invoice", "1"));
        Assert.Equal(0, await cache.GetAsync<int>("tenant-a", "invoice", "2"));
        Assert.Equal(99, await cache.GetAsync<int>("tenant-b", "invoice", "1"));
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

        Assert.Null(await cache.GetAsync<string>("tenant-a", "invoice", "1"));
        Assert.Equal("b", await cache.GetAsync<string>("tenant-b", "invoice", "1"));
    }

    [Fact]
    public async Task Reset_ClearsEveryTenantsData()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default);
        await cache.SetAsync("tenant-b", "invoice", "1", 2, CachePolicy.Default);

        cache.Reset();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, await cache.GetAsync<int>("tenant-a", "invoice", "1"));
        Assert.Equal(0, await cache.GetAsync<int>("tenant-b", "invoice", "1"));
    }

    [Fact]
    public async Task SetAsync_ReSetWithFewerTags_RetiresPriorTagAssociation()
    {
        var cache = new FakeTenantCacheService();

        await cache.SetAsync("tenant-a", "invoice", "1", 1, CachePolicy.Default.WithTags("tag-x"));
        // Re-set the same key with no tags — the prior "tag-x" association must be retired.
        await cache.SetAsync("tenant-a", "invoice", "1", 2, CachePolicy.Default);

        await cache.RemoveByTagAsync("tenant-a", "tag-x");

        // The entry survives because it is no longer associated with "tag-x".
        Assert.Equal(2, await cache.GetAsync<int>("tenant-a", "invoice", "1"));
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
