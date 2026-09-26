using SharedKernel.Execution.Tenancy;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Testing.Caching;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class CachedTenantCatalogTests
{
    private readonly ITenantCatalog _inner = Substitute.For<ITenantCatalog>();
    private readonly FakeCacheService _cache = new();
    private readonly FakeTenantCacheKeyProvider _keyProvider = new();

    [Fact]
    public async Task GetByIdAsync_SecondCall_ServedFromCache()
    {
        var descriptor = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        _inner.GetByIdAsync(descriptor.TenantId, Arg.Any<CancellationToken>()).Returns(descriptor);
        var cached = CreateCatalog();

        var first = await cached.GetByIdAsync(descriptor.TenantId, CancellationToken.None);
        var second = await cached.GetByIdAsync(descriptor.TenantId, CancellationToken.None);

        Assert.Equal(descriptor, first);
        Assert.Equal(descriptor, second);
        await _inner.Received(1).GetByIdAsync(descriptor.TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_StoresUnderServiceScopedKey()
    {
        var descriptor = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        _inner.GetByIdAsync(descriptor.TenantId, Arg.Any<CancellationToken>()).Returns(descriptor);
        var cached = CreateCatalog();

        await cached.GetByIdAsync(descriptor.TenantId, CancellationToken.None);

        var key = _keyProvider.BuildKey("tenant-catalog", descriptor.TenantId.ToString());
        Assert.Equal(CacheLookup<TenantDescriptor?>.Hit(descriptor), await _cache.TryGetAsync<TenantDescriptor?>(key));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownTenant_NullResultIsCachedToo()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        _inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns((TenantDescriptor?)null);
        var cached = CreateCatalog();

        var first = await cached.GetByIdAsync(tenantId, CancellationToken.None);
        var second = await cached.GetByIdAsync(tenantId, CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        await _inner.Received(1).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTenantAsync_ForcesNextLookup_ToHitWrappedCatalog()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var active = Descriptor(tenantId, TenantStatus.Active);
        var suspended = Descriptor(tenantId, TenantStatus.Suspended);
        _inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(active, suspended);
        var cached = CreateCatalog();

        await cached.GetByIdAsync(tenantId, CancellationToken.None);
        await cached.InvalidateTenantAsync(tenantId, CancellationToken.None);
        var afterInvalidation = await cached.GetByIdAsync(tenantId, CancellationToken.None);

        Assert.Equal(TenantStatus.Suspended, afterInvalidation!.Status);
        await _inner.Received(2).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTenantAsync_LeavesOtherTenantsCached()
    {
        var a = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        var b = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        _inner.GetByIdAsync(a.TenantId, Arg.Any<CancellationToken>()).Returns(a);
        _inner.GetByIdAsync(b.TenantId, Arg.Any<CancellationToken>()).Returns(b);
        var cached = CreateCatalog();

        await cached.GetByIdAsync(a.TenantId, CancellationToken.None);
        await cached.GetByIdAsync(b.TenantId, CancellationToken.None);
        await cached.InvalidateTenantAsync(a.TenantId, CancellationToken.None);
        await cached.GetByIdAsync(b.TenantId, CancellationToken.None);

        await _inner.Received(1).GetByIdAsync(b.TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_SecondCall_ServedFromCache()
    {
        const string key = "acme.api.example.com";
        var descriptor = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        _inner.GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>()).Returns(descriptor);
        var cached = CreateCatalog();

        var first = await cached.GetByResolutionKeyAsync(key, CancellationToken.None);
        var second = await cached.GetByResolutionKeyAsync(key, CancellationToken.None);

        Assert.Equal(descriptor, first);
        Assert.Equal(descriptor, second);
        await _inner.Received(1).GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>());
        await _inner.DidNotReceive().GetByIdAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_CachesMappingToTenantId_AndDescriptorById()
    {
        const string key = "acme.api.example.com";
        var descriptor = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        _inner.GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>()).Returns(descriptor);
        var cached = CreateCatalog();

        await cached.GetByResolutionKeyAsync(key, CancellationToken.None);

        var mapping = await _cache.TryGetAsync<Guid?>(_keyProvider.BuildKey("tenant-catalog-resolution", key));
        Assert.Equal(CacheLookup<Guid?>.Hit(descriptor.TenantId.Value), mapping);
        Assert.Equal(descriptor, await cached.GetByIdAsync(descriptor.TenantId, CancellationToken.None));
        await _inner.DidNotReceive().GetByIdAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_AfterInvalidateTenant_ReturnsUpdatedDescriptor()
    {
        const string key = "acme.api.example.com";
        var tenantId = new TenantId(Guid.NewGuid());
        var active = Descriptor(tenantId, TenantStatus.Active);
        var suspended = Descriptor(tenantId, TenantStatus.Suspended);
        _inner.GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>()).Returns(active);
        _inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(suspended);
        var cached = CreateCatalog();

        var before = await cached.GetByResolutionKeyAsync(key, CancellationToken.None);
        await cached.InvalidateTenantAsync(tenantId, CancellationToken.None);
        var after = await cached.GetByResolutionKeyAsync(key, CancellationToken.None);

        Assert.Equal(TenantStatus.Active, before!.Status);
        Assert.Equal(TenantStatus.Suspended, after!.Status);
        await _inner.Received(1).GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>());
        await _inner.Received(1).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_UnknownKey_ReturnsNull_AndCachesTheMiss()
    {
        const string key = "unknown.example.com";
        _inner.GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>()).Returns((TenantDescriptor?)null);
        var cached = CreateCatalog();

        Assert.Null(await cached.GetByResolutionKeyAsync(key, CancellationToken.None));
        Assert.Null(await cached.GetByResolutionKeyAsync(key, CancellationToken.None));

        await _inner.Received(1).GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryCacheCall_UsesTtlPolicyWithoutFailSafeOrEagerRefresh()
    {
        const string key = "acme.api.example.com";
        var descriptor = Descriptor(new TenantId(Guid.NewGuid()), TenantStatus.Active);
        _inner.GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>()).Returns(descriptor);
        _inner.GetByIdAsync(Arg.Any<TenantId>(), Arg.Any<CancellationToken>()).Returns(descriptor);
        var spy = new PolicyRecordingCacheService(_cache);
        var ttl = TimeSpan.FromSeconds(45);
        var cached = new CachedTenantCatalog(_inner, spy, _keyProvider, ttl);

        await cached.GetByResolutionKeyAsync(key, CancellationToken.None);
        await cached.InvalidateTenantAsync(descriptor.TenantId, CancellationToken.None);
        await cached.GetByIdAsync(descriptor.TenantId, CancellationToken.None);

        Assert.Equal(3, spy.Policies.Count);
        Assert.All(spy.Policies, policy =>
        {
            Assert.False(policy.IsFailSafeEnabled);
            Assert.Null(policy.EagerRefreshThreshold);
            Assert.Equal(ttl, policy.L1Duration);
            Assert.Equal(ttl, policy.L2Duration);
        });
    }

    [Fact]
    public void Constructor_DefaultTtl_IsThirtySeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(30), CachedTenantCatalog.DefaultTtl);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveTtl_Throws(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CachedTenantCatalog(_inner, _cache, _keyProvider, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new CachedTenantCatalog(null!, _cache, _keyProvider));
        Assert.Throws<ArgumentNullException>(() => new CachedTenantCatalog(_inner, null!, _keyProvider));
        Assert.Throws<ArgumentNullException>(() => new CachedTenantCatalog(_inner, _cache, null!));
    }

    private CachedTenantCatalog CreateCatalog() => new(_inner, _cache, _keyProvider);

    private static TenantDescriptor Descriptor(TenantId tenantId, TenantStatus status) =>
        new(tenantId, "Acme", status, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

    /// <summary>Delegates to a <see cref="FakeCacheService"/> and records the policy of every write.</summary>
    private sealed class PolicyRecordingCacheService(FakeCacheService inner) : ICacheService
    {
        public List<CachePolicy> Policies { get; } = [];

        public ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default) => inner.TryGetAsync<T>(key, ct);

        public ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default) =>
            inner.TryGetManyAsync<T>(keys, ct);

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
        {
            Policies.Add(policy);
            return inner.GetOrSetAsync(key, factory, policy, ct);
        }

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory, CachePolicy policy, CancellationToken ct = default)
        {
            Policies.Add(policy);
            return inner.GetOrSetAsync(key, factory, policy, ct);
        }

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
        {
            Policies.Add(policy);
            return inner.SetAsync(key, value, policy, ct);
        }

        public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
        {
            Policies.Add(policy);
            return inner.SetManyAsync(entries, policy, ct);
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default) => inner.RemoveAsync(key, ct);

        public ValueTask ExpireAsync(string key, CancellationToken ct = default) => inner.ExpireAsync(key, ct);

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => inner.RemoveByTagAsync(tag, ct);

        public ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => inner.RemoveByTagsAsync(tags, ct);

        public ValueTask ClearAsync(CancellationToken ct = default) => inner.ClearAsync(ct);
    }
}
