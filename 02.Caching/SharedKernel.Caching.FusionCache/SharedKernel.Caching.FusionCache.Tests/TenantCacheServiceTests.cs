using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests for <see cref="TenantCacheService"/> using an in-process FusionCache instance —
/// key isolation across tenants, tag isolation (tenant-to-tenant and global-to-tenant),
/// tenant-wide removal, round-trip behaviour, and DI registration via
/// <see cref="TenantCacheServiceCachingBuilderExtensions.AddTenantCacheService"/>.
/// </summary>
public sealed class TenantCacheServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITenantCacheService _tenantCache;
    private readonly ICacheService _cache;

    public TenantCacheServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddTenantCacheService();
        _provider = services.BuildServiceProvider();
        _tenantCache = _provider.GetRequiredService<ITenantCacheService>();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -------------------------------------------------------------------------
    // Round-trip behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsStoredValue()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "42", "hello", CachePolicy.Default);

        var result = await _tenantCache.TryGetAsync<string>("tenant-a", "invoice", "42");

        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task TryGetAsync_UnknownEntry_IsMiss()
    {
        var result = await _tenantCache.TryGetAsync<string>("tenant-a", "invoice", "unknown-" + Guid.NewGuid());

        Assert.False(result.IsHit);
    }

    [Fact]
    public async Task SetAsync_StoresUnderTenantScopedKey()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "key-shape", "hello", CachePolicy.Default);

        Assert.True((await _cache.TryGetAsync<string>("test-svc:@tenant-a:invoice:key-shape")).IsHit);
        Assert.False((await _cache.TryGetAsync<string>("test-svc:invoice:key-shape")).IsHit);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_InvokesFactoryOnceAndCaches()
    {
        var factoryCalls = 0;

        async ValueTask<string> Factory(CancellationToken _)
        {
            factoryCalls++;
            await Task.Yield();
            return "computed";
        }

        var first = await _tenantCache.GetOrSetAsync<string>("tenant-a", "invoice", "gos-1", Factory, CachePolicy.Default);
        var second = await _tenantCache.GetOrSetAsync<string>("tenant-a", "invoice", "gos-1", Factory, CachePolicy.Default);

        Assert.Equal("computed", first);
        Assert.Equal("computed", second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ContextOverload_SkipCaching_DoesNotStore()
    {
        var factoryCalls = 0;

        ValueTask<string> Factory(CacheFactoryContext context, CancellationToken _)
        {
            factoryCalls++;
            context.SkipCaching();
            return ValueTask.FromResult("not-cached");
        }

        Assert.Equal("not-cached", await _tenantCache.GetOrSetAsync<string>("tenant-a", "invoice", "skip-1", Factory, CachePolicy.Default));
        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "invoice", "skip-1")).IsHit);
        Assert.Equal("not-cached", await _tenantCache.GetOrSetAsync<string>("tenant-a", "invoice", "skip-1", Factory, CachePolicy.Default));
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "rm-1", "value", CachePolicy.Default);

        await _tenantCache.RemoveAsync("tenant-a", "invoice", "rm-1");

        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "invoice", "rm-1")).IsHit);
    }

    [Fact]
    public async Task ExpireAsync_NextGetOrSetRecomputes()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "exp-1", "old", CachePolicy.Default.WithoutFailSafe());

        await _tenantCache.ExpireAsync("tenant-a", "invoice", "exp-1");

        var result = await _tenantCache.GetOrSetAsync<string>(
            "tenant-a", "invoice", "exp-1", _ => ValueTask.FromResult("new"), CachePolicy.Default.WithoutFailSafe());
        Assert.Equal("new", result);
    }

    // -------------------------------------------------------------------------
    // Tenant key isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_SameEntityAndId_DifferentTenants_DoNotCollide()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "42", "value-for-a", CachePolicy.Default);
        await _tenantCache.SetAsync("tenant-b", "invoice", "42", "value-for-b", CachePolicy.Default);

        Assert.Equal("value-for-a", (await _tenantCache.TryGetAsync<string>("tenant-a", "invoice", "42")).Value);
        Assert.Equal("value-for-b", (await _tenantCache.TryGetAsync<string>("tenant-b", "invoice", "42")).Value);
    }

    [Fact]
    public async Task RemoveAsync_OneTenant_DoesNotAffectAnotherTenantsEntryForSameEntityAndId()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "shared-id", "value-for-a", CachePolicy.Default);
        await _tenantCache.SetAsync("tenant-b", "invoice", "shared-id", "value-for-b", CachePolicy.Default);

        await _tenantCache.RemoveAsync("tenant-a", "invoice", "shared-id");

        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "invoice", "shared-id")).IsHit);
        Assert.Equal("value-for-b", (await _tenantCache.TryGetAsync<string>("tenant-b", "invoice", "shared-id")).Value);
    }

    // -------------------------------------------------------------------------
    // Tag isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByTagAsync_TenantA_DoesNotEvictTenantB_SameTagName()
    {
        var policy = CachePolicy.Default.WithTags("orders");

        await _tenantCache.SetAsync("tenant-a", "order", "1", "a-order-1", policy);
        await _tenantCache.SetAsync("tenant-b", "order", "1", "b-order-1", policy);

        await _tenantCache.RemoveByTagAsync("tenant-a", "orders");

        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "order", "1")).IsHit);
        Assert.Equal("b-order-1", (await _tenantCache.TryGetAsync<string>("tenant-b", "order", "1")).Value);
    }

    [Fact]
    public async Task RemoveByTagAsync_ScopedToOwnTenant_EvictsAllOwnTaggedEntries()
    {
        var policy = CachePolicy.Default.WithTags("orders");

        await _tenantCache.SetAsync("tenant-a", "order", "1", "a-1", policy);
        await _tenantCache.SetAsync("tenant-a", "order", "2", "a-2", policy);
        await _tenantCache.SetAsync("tenant-a", "order", "3", "a-3", CachePolicy.Default); // untagged

        await _tenantCache.RemoveByTagAsync("tenant-a", "orders");

        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "order", "1")).IsHit);
        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "order", "2")).IsHit);
        Assert.Equal("a-3", (await _tenantCache.TryGetAsync<string>("tenant-a", "order", "3")).Value);
    }

    [Fact]
    public async Task GlobalRemoveByTag_WithSameTagName_DoesNotEvictTenantEntries()
    {
        var policy = CachePolicy.Default.WithTags("orders");

        await _tenantCache.SetAsync("tenant-a", "order", "g-1", "tenant-value", policy);
        await _cache.SetAsync("test-svc:order:g-1", "global-value", policy);

        await _cache.RemoveByTagAsync("orders");

        Assert.False((await _cache.TryGetAsync<string>("test-svc:order:g-1")).IsHit);
        Assert.Equal("tenant-value", (await _tenantCache.TryGetAsync<string>("tenant-a", "order", "g-1")).Value);
    }

    [Fact]
    public async Task GlobalRemoveByTag_NamedLikeATenantTag_CannotEvictTenantEntries()
    {
        // Tenant tags carry the '@' marker, which a global policy tag may not start with, so a
        // global tag spelled "{tenant}:{tag}" is a different tag from the tenant's own.
        await _tenantCache.SetAsync("tenant-a", "order", "g-2", "tenant-value", CachePolicy.Default.WithTags("orders"));
        await _cache.SetAsync("test-svc:order:g-2", "global-value", CachePolicy.Default.WithTags("tenant-a:orders", "tenant-a"));

        await _cache.RemoveByTagAsync("tenant-a:orders");
        await _cache.RemoveByTagAsync("tenant-a");

        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags("@tenant-a:orders"));
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags("@tenant-a"));
        Assert.False((await _cache.TryGetAsync<string>("test-svc:order:g-2")).IsHit);
        Assert.Equal("tenant-value", (await _tenantCache.TryGetAsync<string>("tenant-a", "order", "g-2")).Value);
    }

    [Fact]
    public async Task RemoveTenantAsync_RemovesAllEntriesOfThatTenantOnly()
    {
        await _tenantCache.SetAsync("tenant-a", "order", "1", "a-tagged", CachePolicy.Default.WithTags("orders"));
        await _tenantCache.SetAsync("tenant-a", "customer", "2", "a-untagged", CachePolicy.Default);
        await _tenantCache.SetAsync("tenant-b", "order", "1", "b-tagged", CachePolicy.Default.WithTags("orders"));
        await _tenantCache.SetAsync("tenant-b", "customer", "2", "b-untagged", CachePolicy.Default);
        await _cache.SetAsync("test-svc:order:1", "global", CachePolicy.Default);

        await _tenantCache.RemoveTenantAsync("tenant-a");

        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "order", "1")).IsHit);
        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "customer", "2")).IsHit);
        Assert.Equal("b-tagged", (await _tenantCache.TryGetAsync<string>("tenant-b", "order", "1")).Value);
        Assert.Equal("b-untagged", (await _tenantCache.TryGetAsync<string>("tenant-b", "customer", "2")).Value);
        Assert.Equal("global", (await _cache.TryGetAsync<string>("test-svc:order:1")).Value);
    }

    [Fact]
    public async Task RemoveTenantAsync_AppliesToEntriesWrittenThroughGetOrSet()
    {
        await _tenantCache.GetOrSetAsync<string>("tenant-a", "order", "gos-t", _ => ValueTask.FromResult("a"), CachePolicy.Default);

        await _tenantCache.RemoveTenantAsync("tenant-a");

        Assert.False((await _tenantCache.TryGetAsync<string>("tenant-a", "order", "gos-t")).IsHit);
    }

    // -------------------------------------------------------------------------
    // DI registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddTenantCacheService_RegistersSingleton()
    {
        Assert.IsType<TenantCacheService>(_tenantCache);
        Assert.Same(_tenantCache, _provider.GetRequiredService<ITenantCacheService>());
    }

    [Fact]
    public void AddTenantCacheService_IsIdempotent_CalledTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheService()
                .AddTenantCacheService();

        Assert.Single(services, d => d.ServiceType == typeof(ITenantCacheService));
    }

    [Fact]
    public void AddTenantCacheService_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TenantCacheServiceCachingBuilderExtensions.AddTenantCacheService(null!));
    }

    // -------------------------------------------------------------------------
    // Argument validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryGetAsync_NullOrWhitespaceTenantId_ThrowsArgumentException(string tenantId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tenantCache.TryGetAsync<string>(tenantId, "invoice", "1").AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByTagAsync_NullOrWhitespaceTenantIdOrTag_ThrowsArgumentException(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tenantCache.RemoveByTagAsync(value, "some-tag").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _tenantCache.RemoveByTagAsync("tenant-a", value).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _tenantCache.RemoveTenantAsync(value).AsTask());
    }

    [Fact]
    public async Task SetAsync_NullPolicy_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _tenantCache.SetAsync("tenant-a", "invoice", "1", "value", null!).AsTask());
    }

    [Fact]
    public async Task SetAsync_PolicyAlreadyScopedToTenant_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _tenantCache.SetAsync("tenant-a", "invoice", "1", "value", CachePolicy.Default.ForTenant("tenant-a")).AsTask());
    }
}
