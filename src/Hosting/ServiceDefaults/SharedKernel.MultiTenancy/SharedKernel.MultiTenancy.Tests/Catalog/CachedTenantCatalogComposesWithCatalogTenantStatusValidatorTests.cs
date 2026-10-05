using NSubstitute;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Testing.Caching;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class CachedTenantCatalogComposesWithCatalogTenantStatusValidatorTests
{
    [Fact]
    public async Task CatalogTenantStatusValidator_ComposesWithCachedTenantCatalog_WithZeroCodeChanges()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var descriptor = new TenantDescriptor(
            tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(descriptor);

        // Both constructors accept the ITenantCatalog interface only — this compiles and behaves
        // correctly with zero changes to either type.
        var validator = new CatalogTenantStatusValidator(
            new CachedTenantCatalog(inner, new FakeCacheService(), new FakeTenantCacheKeyProvider()));

        var isActive = await validator.IsActiveAsync(tenantId, CancellationToken.None);

        Assert.True(isActive);
    }
}
