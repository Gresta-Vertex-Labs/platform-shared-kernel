using NSubstitute;
using SharedKernel.MultiTenancy.Catalog;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class CachedTenantCatalogComposesWithCatalogTenantStatusValidatorTests
{
    [Fact]
    public async Task CatalogTenantStatusValidator_ComposesWithCachedTenantCatalog_WithZeroCodeChanges()
    {
        var tenantId = Guid.NewGuid();
        var descriptor = new TenantDescriptor(
            tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, null, new Dictionary<string, string>());

        var inner = Substitute.For<ITenantCatalog>();
        inner.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(descriptor);

        // Both constructors accept the ITenantCatalog interface only — this compiles and behaves
        // correctly with zero changes to either type, per P-472's own explicit acceptance criterion.
        var validator = new CatalogTenantStatusValidator(new CachedTenantCatalog(inner));

        var isActive = await validator.IsActiveAsync(tenantId, CancellationToken.None);

        Assert.True(isActive);
    }
}
