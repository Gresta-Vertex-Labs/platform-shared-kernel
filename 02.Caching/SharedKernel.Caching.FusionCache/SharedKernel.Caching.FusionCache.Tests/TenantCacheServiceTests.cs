using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Execution.Tenancy;
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
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    private static readonly TenantId TenantB = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

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
        await _tenantCache.SetAsync(TenantA, "invoice", "42", "hello", CachePolicy.Default);

        var result = await _tenantCache.TryGetAsync<string>(TenantA, "invoice", "42");

        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task TryGetAsync_UnknownEntry_IsMiss()
    {
        var result = await _tenantCache.TryGetAsync<string>(TenantA, "invoice", "unknown-" + Guid.NewGuid());

        Assert.False(result.IsHit);
    }

    [Fact]
    public async Task SetAsync_StoresUnderTenantScopedKey()
    {
        await _tenantCache.SetAsync(TenantA, "invoice", "key-shape", "hello", CachePolicy.Default);

        Assert.True((await _cache.TryGetAsync<string>($"test-svc:@{TenantA}:invoice:key-shape")).IsHit);
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

        var first = await _tenantCache.GetOrSetAsync<string>(TenantA, "invoice", "gos-1", Factory, CachePolicy.Default);
        var second = await _tenantCache.GetOrSetAsync<string>(TenantA, "invoice", "gos-1", Factory, CachePolicy.Default);

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

        Assert.Equal("not-cached", await _tenantCache.GetOrSetAsync<string>(TenantA, "invoice", "skip-1", Factory, CachePolicy.Default));
        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "invoice", "skip-1")).IsHit);
        Assert.Equal("not-cached", await _tenantCache.GetOrSetAsync<string>(TenantA, "invoice", "skip-1", Factory, CachePolicy.Default));
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        await _tenantCache.SetAsync(TenantA, "invoice", "rm-1", "value", CachePolicy.Default);

        await _tenantCache.RemoveAsync(TenantA, "invoice", "rm-1");

        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "invoice", "rm-1")).IsHit);
    }

    [Fact]
    public async Task ExpireAsync_NextGetOrSetRecomputes()
    {
        await _tenantCache.SetAsync(TenantA, "invoice", "exp-1", "old", CachePolicy.Default.WithoutFailSafe());

        await _tenantCache.ExpireAsync(TenantA, "invoice", "exp-1");

        var result = await _tenantCache.GetOrSetAsync<string>(
            TenantA, "invoice", "exp-1", _ => ValueTask.FromResult("new"), CachePolicy.Default.WithoutFailSafe());
        Assert.Equal("new", result);
    }

    // -------------------------------------------------------------------------
    // Tenant key isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_SameEntityAndId_DifferentTenants_DoNotCollide()
    {
        await _tenantCache.SetAsync(TenantA, "invoice", "42", "value-for-a", CachePolicy.Default);
        await _tenantCache.SetAsync(TenantB, "invoice", "42", "value-for-b", CachePolicy.Default);

        Assert.Equal("value-for-a", (await _tenantCache.TryGetAsync<string>(TenantA, "invoice", "42")).Value);
        Assert.Equal("value-for-b", (await _tenantCache.TryGetAsync<string>(TenantB, "invoice", "42")).Value);
    }

    [Fact]
    public async Task RemoveAsync_OneTenant_DoesNotAffectAnotherTenantsEntryForSameEntityAndId()
    {
        await _tenantCache.SetAsync(TenantA, "invoice", "shared-id", "value-for-a", CachePolicy.Default);
        await _tenantCache.SetAsync(TenantB, "invoice", "shared-id", "value-for-b", CachePolicy.Default);

        await _tenantCache.RemoveAsync(TenantA, "invoice", "shared-id");

        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "invoice", "shared-id")).IsHit);
        Assert.Equal("value-for-b", (await _tenantCache.TryGetAsync<string>(TenantB, "invoice", "shared-id")).Value);
    }

    // -------------------------------------------------------------------------
    // Tag isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByTagAsync_TenantA_DoesNotEvictTenantB_SameTagName()
    {
        var policy = CachePolicy.Default.WithTags("orders");

        await _tenantCache.SetAsync(TenantA, "order", "1", "a-order-1", policy);
        await _tenantCache.SetAsync(TenantB, "order", "1", "b-order-1", policy);

        await _tenantCache.RemoveByTagAsync(TenantA, "orders");

        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "order", "1")).IsHit);
        Assert.Equal("b-order-1", (await _tenantCache.TryGetAsync<string>(TenantB, "order", "1")).Value);
    }

    [Fact]
    public async Task RemoveByTagAsync_ScopedToOwnTenant_EvictsAllOwnTaggedEntries()
    {
        var policy = CachePolicy.Default.WithTags("orders");

        await _tenantCache.SetAsync(TenantA, "order", "1", "a-1", policy);
        await _tenantCache.SetAsync(TenantA, "order", "2", "a-2", policy);
        await _tenantCache.SetAsync(TenantA, "order", "3", "a-3", CachePolicy.Default); // untagged

        await _tenantCache.RemoveByTagAsync(TenantA, "orders");

        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "order", "1")).IsHit);
        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "order", "2")).IsHit);
        Assert.Equal("a-3", (await _tenantCache.TryGetAsync<string>(TenantA, "order", "3")).Value);
    }

    [Fact]
    public async Task GlobalRemoveByTag_WithSameTagName_DoesNotEvictTenantEntries()
    {
        var policy = CachePolicy.Default.WithTags("orders");

        await _tenantCache.SetAsync(TenantA, "order", "g-1", "tenant-value", policy);
        await _cache.SetAsync("test-svc:order:g-1", "global-value", policy);

        await _cache.RemoveByTagAsync("orders");

        Assert.False((await _cache.TryGetAsync<string>("test-svc:order:g-1")).IsHit);
        Assert.Equal("tenant-value", (await _tenantCache.TryGetAsync<string>(TenantA, "order", "g-1")).Value);
    }

    [Fact]
    public async Task GlobalRemoveByTag_NamedLikeATenantTag_CannotEvictTenantEntries()
    {
        // Tenant tags carry the '@' marker, which a global policy tag may not start with, so a
        // global tag spelled "{tenant}:{tag}" is a different tag from the tenant's own.
        await _tenantCache.SetAsync(TenantA, "order", "g-2", "tenant-value", CachePolicy.Default.WithTags("orders"));
        await _cache.SetAsync("test-svc:order:g-2", "global-value", CachePolicy.Default.WithTags($"{TenantA}:orders", TenantA.ToString()));

        await _cache.RemoveByTagAsync($"{TenantA}:orders");
        await _cache.RemoveByTagAsync(TenantA.ToString());

        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags($"@{TenantA}:orders"));
        Assert.Throws<ArgumentException>(() => CachePolicy.Default.WithTags($"@{TenantA}"));
        Assert.False((await _cache.TryGetAsync<string>("test-svc:order:g-2")).IsHit);
        Assert.Equal("tenant-value", (await _tenantCache.TryGetAsync<string>(TenantA, "order", "g-2")).Value);
    }

    [Fact]
    public async Task RemoveTenantAsync_RemovesAllEntriesOfThatTenantOnly()
    {
        await _tenantCache.SetAsync(TenantA, "order", "1", "a-tagged", CachePolicy.Default.WithTags("orders"));
        await _tenantCache.SetAsync(TenantA, "customer", "2", "a-untagged", CachePolicy.Default);
        await _tenantCache.SetAsync(TenantB, "order", "1", "b-tagged", CachePolicy.Default.WithTags("orders"));
        await _tenantCache.SetAsync(TenantB, "customer", "2", "b-untagged", CachePolicy.Default);
        await _cache.SetAsync("test-svc:order:1", "global", CachePolicy.Default);

        await _tenantCache.RemoveTenantAsync(TenantA);

        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "order", "1")).IsHit);
        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "customer", "2")).IsHit);
        Assert.Equal("b-tagged", (await _tenantCache.TryGetAsync<string>(TenantB, "order", "1")).Value);
        Assert.Equal("b-untagged", (await _tenantCache.TryGetAsync<string>(TenantB, "customer", "2")).Value);
        Assert.Equal("global", (await _cache.TryGetAsync<string>("test-svc:order:1")).Value);
    }

    [Fact]
    public async Task RemoveTenantAsync_AppliesToEntriesWrittenThroughGetOrSet()
    {
        await _tenantCache.GetOrSetAsync<string>(TenantA, "order", "gos-t", _ => ValueTask.FromResult("a"), CachePolicy.Default);

        await _tenantCache.RemoveTenantAsync(TenantA);

        Assert.False((await _tenantCache.TryGetAsync<string>(TenantA, "order", "gos-t")).IsHit);
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

    [Fact]
    public async Task TryGetAsync_DefaultTenantId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tenantCache.TryGetAsync<string>(default, "invoice", "1").AsTask());
    }

    [Fact]
    public async Task RemoveTenantAsync_DefaultTenantId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tenantCache.RemoveByTagAsync(default, "some-tag").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _tenantCache.RemoveTenantAsync(default).AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveByTagAsync_NullOrWhitespaceTag_ThrowsArgumentException(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tenantCache.RemoveByTagAsync(TenantA, value).AsTask());
    }

    [Fact]
    public async Task SetAsync_NullPolicy_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _tenantCache.SetAsync(TenantA, "invoice", "1", "value", null!).AsTask());
    }

    [Fact]
    public async Task SetAsync_PolicyAlreadyScopedToTenant_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _tenantCache.SetAsync(TenantA, "invoice", "1", "value", CachePolicy.Default.ForTenant(TenantA)).AsTask());
    }
}
