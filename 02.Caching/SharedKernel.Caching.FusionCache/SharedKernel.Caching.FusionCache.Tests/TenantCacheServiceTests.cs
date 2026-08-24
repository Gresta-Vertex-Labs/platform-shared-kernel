using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests for <see cref="TenantCacheService"/> using an in-process FusionCache instance —
/// key isolation across tenants, cross-tenant tag-invalidation isolation, round-trip behaviour,
/// and DI registration via <see cref="TenantCacheServiceCachingBuilderExtensions.AddTenantCacheService"/>.
/// </summary>
public sealed class TenantCacheServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITenantCacheService _tenantCache;

    public TenantCacheServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddTenantCacheService();
        _provider = services.BuildServiceProvider();
        _tenantCache = _provider.GetRequiredService<ITenantCacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -------------------------------------------------------------------------
    // Round-trip behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsStoredValue()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "42", "hello", CachePolicy.Default);

        var result = await _tenantCache.GetAsync<string>("tenant-a", "invoice", "42");

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GetAsync_UnknownEntry_ReturnsNull()
    {
        var result = await _tenantCache.GetAsync<string>("tenant-a", "invoice", "unknown-" + Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_InvokesFactoryAndCaches()
    {
        var factoryCalls = 0;

        var result = await _tenantCache.GetOrSetAsync<string>(
            "tenant-a",
            "invoice",
            "gos-1",
            async _ =>
            {
                factoryCalls++;
                await Task.Yield();
                return "computed";
            },
            CachePolicy.Default);

        Assert.Equal("computed", result);
        Assert.Equal(1, factoryCalls);

        // Second call is a cache hit — factory not invoked again.
        var second = await _tenantCache.GetOrSetAsync<string>(
            "tenant-a",
            "invoice",
            "gos-1",
            async _ =>
            {
                factoryCalls++;
                await Task.Yield();
                return "computed";
            },
            CachePolicy.Default);

        Assert.Equal("computed", second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "rm-1", "value", CachePolicy.Default);

        await _tenantCache.RemoveAsync("tenant-a", "invoice", "rm-1");

        var result = await _tenantCache.GetAsync<string>("tenant-a", "invoice", "rm-1");
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // TC-05 — Tenant key isolation: two tenants, same (entity, id), never collide
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_SameEntityAndId_DifferentTenants_DoNotCollide()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "42", "value-for-a", CachePolicy.Default);
        await _tenantCache.SetAsync("tenant-b", "invoice", "42", "value-for-b", CachePolicy.Default);

        var forA = await _tenantCache.GetAsync<string>("tenant-a", "invoice", "42");
        var forB = await _tenantCache.GetAsync<string>("tenant-b", "invoice", "42");

        Assert.Equal("value-for-a", forA);
        Assert.Equal("value-for-b", forB);
    }

    [Fact]
    public async Task RemoveAsync_OneTenant_DoesNotAffectAnotherTenantsEntryForSameEntityAndId()
    {
        await _tenantCache.SetAsync("tenant-a", "invoice", "shared-id", "value-for-a", CachePolicy.Default);
        await _tenantCache.SetAsync("tenant-b", "invoice", "shared-id", "value-for-b", CachePolicy.Default);

        await _tenantCache.RemoveAsync("tenant-a", "invoice", "shared-id");

        Assert.Null(await _tenantCache.GetAsync<string>("tenant-a", "invoice", "shared-id"));
        Assert.Equal("value-for-b", await _tenantCache.GetAsync<string>("tenant-b", "invoice", "shared-id"));
    }

    // -------------------------------------------------------------------------
    // TC-06 — Cross-tenant tag isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByTagAsync_TenantB_DoesNotEvictTenantA_SameTagName()
    {
        const string tag = "orders";
        var policy = CachePolicy.Default.WithTags(tag);

        await _tenantCache.SetAsync("tenant-a", "order", "1", "a-order-1", policy);
        await _tenantCache.SetAsync("tenant-b", "order", "1", "b-order-1", policy);

        // Tenant B invalidates its own "orders" tag.
        await _tenantCache.RemoveByTagAsync("tenant-b", tag);

        // Tenant A's identically-named tag must survive untouched.
        Assert.Equal("a-order-1", await _tenantCache.GetAsync<string>("tenant-a", "order", "1"));

        // Tenant B's entry is gone.
        Assert.Null(await _tenantCache.GetAsync<string>("tenant-b", "order", "1"));
    }

    [Fact]
    public async Task RemoveByTagAsync_ScopedToOwnTenant_EvictsAllOwnTaggedEntries()
    {
        const string tag = "orders";
        var policy = CachePolicy.Default.WithTags(tag);

        await _tenantCache.SetAsync("tenant-a", "order", "1", "a-1", policy);
        await _tenantCache.SetAsync("tenant-a", "order", "2", "a-2", policy);
        await _tenantCache.SetAsync("tenant-a", "order", "3", "a-3", CachePolicy.Default); // untagged

        await _tenantCache.RemoveByTagAsync("tenant-a", tag);

        Assert.Null(await _tenantCache.GetAsync<string>("tenant-a", "order", "1"));
        Assert.Null(await _tenantCache.GetAsync<string>("tenant-a", "order", "2"));
        Assert.Equal("a-3", await _tenantCache.GetAsync<string>("tenant-a", "order", "3"));
    }

    // -------------------------------------------------------------------------
    // TC-07 — Additive DI registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddTenantCacheService_RegistersITenantCacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheService();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ITenantCacheService>());
    }

    [Fact]
    public void AddTenantCacheService_DoesNotRemoveOrOverride_PriorAddTenantCacheKeyProviderRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheKeyProvider();

        using var providerBefore = services.BuildServiceProvider();
        var keyProviderBefore = providerBefore.GetRequiredService<ITenantCacheKeyProvider>();

        // Now additively call AddTenantCacheService on the SAME service collection.
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheService();

        using var providerAfter = services.BuildServiceProvider();
        var keyProviderAfter = providerAfter.GetRequiredService<ITenantCacheKeyProvider>();
        var tenantCacheService = providerAfter.GetRequiredService<ITenantCacheService>();

        // The ITenantCacheKeyProvider registration type is unaffected — still the same
        // implementation type registered by the original standalone AddTenantCacheKeyProvider()
        // call, not replaced or removed by the later AddTenantCacheService() call.
        Assert.IsType<TenantCacheKeyProvider>(keyProviderBefore);
        Assert.IsType<TenantCacheKeyProvider>(keyProviderAfter);
        Assert.NotNull(tenantCacheService);
    }

    [Fact]
    public void AddTenantCacheService_WithoutPriorAddTenantCacheKeyProvider_StillRegistersKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheService();

        using var provider = services.BuildServiceProvider();

        // AddTenantCacheService alone (with no standalone AddTenantCacheKeyProvider() call) still
        // makes ITenantCacheKeyProvider resolvable — TenantCacheService depends on it internally.
        Assert.NotNull(provider.GetService<ITenantCacheKeyProvider>());
        Assert.NotNull(provider.GetService<ITenantCacheService>());
    }

    [Fact]
    public void AddTenantCacheService_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheService();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ITenantCacheService>();
        var second = provider.GetRequiredService<ITenantCacheService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddTenantCacheService_IsIdempotent_CalledTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc")
                .AddTenantCacheService()
                .AddTenantCacheService();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ITenantCacheService>());
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
    public async Task GetAsync_NullOrWhitespaceTenantId_ThrowsArgumentException(string tenantId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tenantCache.GetAsync<string>(tenantId, "invoice", "1").AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByTagAsync_NullOrWhitespaceTenantId_ThrowsArgumentException(string tenantId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tenantCache.RemoveByTagAsync(tenantId, "some-tag").AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByTagAsync_NullOrWhitespaceTag_ThrowsArgumentException(string tag)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tenantCache.RemoveByTagAsync("tenant-a", tag).AsTask());
    }

    [Fact]
    public async Task SetAsync_NullPolicy_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _tenantCache.SetAsync("tenant-a", "invoice", "1", "value", null!).AsTask());
    }
}
