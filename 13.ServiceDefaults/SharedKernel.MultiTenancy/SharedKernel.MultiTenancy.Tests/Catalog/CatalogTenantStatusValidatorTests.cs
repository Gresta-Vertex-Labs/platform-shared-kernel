using NSubstitute;
using SharedKernel.MultiTenancy.Catalog;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class CatalogTenantStatusValidatorTests
{
    [Fact]
    public async Task IsActiveAsync_WithActiveTenant_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .GetByIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantDescriptor(
                tenantId,
                "Acme",
                TenantStatus.Active,
                TenantIsolationMode.Shared,
                null,
                new Dictionary<string, string>()));

        var validator = new CatalogTenantStatusValidator(catalog);

        var result = await validator.IsActiveAsync(tenantId, CancellationToken.None);

        Assert.True(result);
    }

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Offboarded)]
    public async Task IsActiveAsync_WithSuspendedOrOffboardedTenant_ReturnsFalse(TenantStatus status)
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .GetByIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantDescriptor(
                tenantId,
                "Acme",
                status,
                TenantIsolationMode.Shared,
                null,
                new Dictionary<string, string>()));

        var validator = new CatalogTenantStatusValidator(catalog);

        var result = await validator.IsActiveAsync(tenantId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsActiveAsync_WithTenantAbsentFromCatalog_ReturnsFalse_FailClosed()
    {
        var tenantId = Guid.NewGuid();
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .GetByIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns((TenantDescriptor?)null);

        var validator = new CatalogTenantStatusValidator(catalog);

        var result = await validator.IsActiveAsync(tenantId, CancellationToken.None);

        Assert.False(result);
    }
}
