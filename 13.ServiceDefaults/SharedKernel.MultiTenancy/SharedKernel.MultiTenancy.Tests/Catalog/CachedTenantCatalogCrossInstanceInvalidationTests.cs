using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.MultiTenancy.Catalog;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class CachedTenantCatalogCrossInstanceInvalidationTests
{
    [Fact]
    public async Task InvalidateTenantAsync_WithCrossInstanceInvalidationConfigured_PublishesExactlyOneSignal()
    {
        var tenantId = Guid.NewGuid();
        var inner = Substitute.For<ITenantCatalog>();
        var bus = Substitute.For<ICacheInvalidationBus>();

        var cached = new CachedTenantCatalog(inner).WithCrossInstanceInvalidation(bus);

        await cached.InvalidateTenantAsync(tenantId, CancellationToken.None);

        await bus.Received(1).PublishKeyInvalidationAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTenantAsync_WithoutCrossInstanceInvalidationConfigured_NeverPublishes()
    {
        var tenantId = Guid.NewGuid();
        var inner = Substitute.For<ITenantCatalog>();

        var cached = new CachedTenantCatalog(inner);

        // Must not throw despite no bus ever having been configured.
        await cached.InvalidateTenantAsync(tenantId, CancellationToken.None);
    }

    [Fact]
    public async Task HandleCrossInstanceInvalidationSignal_RemovesLocalEntry_WithoutRePublishing()
    {
        var tenantId = Guid.NewGuid();
        var descriptor = new TenantDescriptor(
            tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(descriptor);

        var bus = Substitute.For<ICacheInvalidationBus>();
        var cached = new CachedTenantCatalog(inner).WithCrossInstanceInvalidation(bus);

        // Warm the cache.
        await cached.GetByIdAsync(tenantId, CancellationToken.None);

        // Simulate a signal received from another replica (the consumer's own subscription
        // wiring calling this method) — never a real Redis dependency in this domain's own tests.
        cached.HandleCrossInstanceInvalidationSignal(tenantId);

        await cached.GetByIdAsync(tenantId, CancellationToken.None);

        // One warm-up call plus one post-signal call: the local entry was genuinely evicted.
        await inner.Received(2).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());

        // Receiving a signal must never itself publish — that would echo the signal back onto the
        // bus and loop forever across replicas.
        await bus.DidNotReceive().PublishKeyInvalidationAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }
}
