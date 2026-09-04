using NSubstitute;
using SharedKernel.MultiTenancy.Catalog;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class CachedTenantCatalogTests
{
    [Fact]
    public async Task GetByIdAsync_WithinTtl_DoesNotHitWrappedCatalogAgain()
    {
        var tenantId = Guid.NewGuid();
        var descriptor = new TenantDescriptor(
            tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(descriptor);

        var timeProvider = new FakeTimeProvider();
        var cached = new CachedTenantCatalog(inner, TimeSpan.FromSeconds(30), timeProvider);

        var first = await cached.GetByIdAsync(tenantId, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var second = await cached.GetByIdAsync(tenantId, CancellationToken.None);

        Assert.Equal(descriptor, first);
        Assert.Equal(descriptor, second);
        await inner.Received(1).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_AfterTtlExpires_HitsWrappedCatalogAgain()
    {
        var tenantId = Guid.NewGuid();
        var descriptor = new TenantDescriptor(
            tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(descriptor);

        var timeProvider = new FakeTimeProvider();
        var cached = new CachedTenantCatalog(inner, TimeSpan.FromSeconds(30), timeProvider);

        await cached.GetByIdAsync(tenantId, CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        await cached.GetByIdAsync(tenantId, CancellationToken.None);

        await inner.Received(2).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTenantAsync_ForcesNextLookup_ToHitWrappedCatalog_EvenInsideTtlWindow()
    {
        var tenantId = Guid.NewGuid();
        var descriptor = new TenantDescriptor(
            tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(descriptor);

        var timeProvider = new FakeTimeProvider();
        var cached = new CachedTenantCatalog(inner, TimeSpan.FromSeconds(30), timeProvider);

        await cached.GetByIdAsync(tenantId, CancellationToken.None);

        // Still well within the 30s TTL window.
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await cached.InvalidateTenantAsync(tenantId, CancellationToken.None);

        await cached.GetByIdAsync(tenantId, CancellationToken.None);

        await inner.Received(2).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_WithinTtl_DoesNotHitWrappedCatalogAgain()
    {
        const string key = "acme.api.example.com";
        var descriptor = new TenantDescriptor(
            Guid.NewGuid(), "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>()).Returns(descriptor);

        var cached = new CachedTenantCatalog(inner);

        await cached.GetByResolutionKeyAsync(key, CancellationToken.None);
        await cached.GetByResolutionKeyAsync(key, CancellationToken.None);

        await inner.Received(1).GetByResolutionKeyAsync(key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_WithNonPositiveTtl_Throws()
    {
        var inner = Substitute.For<ITenantCatalog>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new CachedTenantCatalog(inner, TimeSpan.Zero));
    }

    /// <summary>A minimal, test-controllable <see cref="TimeProvider"/>.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
