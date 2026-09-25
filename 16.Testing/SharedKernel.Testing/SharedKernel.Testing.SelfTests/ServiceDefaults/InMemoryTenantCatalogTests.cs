using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.Testing.ServiceDefaults;

namespace SharedKernel.Testing.SelfTests.ServiceDefaults;

/// <summary>
/// Proves <see cref="InMemoryTenantCatalog"/> genuinely implements <see cref="ITenantCatalog"/> —
/// seed-by-id, seed-by-resolution-key, mid-test status mutation, unseeded-returns-null for both
/// lookup members, <see cref="InMemoryTenantCatalog.Reset"/>, and composition with the real
/// <see cref="CatalogTenantStatusValidator"/> with zero code changes on either side. No consuming
/// service has adopted this fake yet, so this self-test is the only behavioral proof today, per the
/// SelfTests routing rule.
/// </summary>
public sealed class InMemoryTenantCatalogTests
{
    private static TenantDescriptor CreateDescriptor(
        TenantId? tenantId = null,
        string displayName = "Acme Corp",
        TenantStatus status = TenantStatus.Active,
        TenantIsolationMode isolationMode = TenantIsolationMode.Shared,
        string? defaultCulture = null) =>
        new(
            tenantId ?? new TenantId(Guid.NewGuid()),
            displayName,
            status,
            isolationMode,
            defaultCulture,
            new Dictionary<string, string>());

    [Fact]
    public async Task GetByIdAsync_SeededTenant_ReturnsDescriptor()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor();
        catalog.SeedTenant(descriptor);

        var result = await catalog.GetByIdAsync(descriptor.TenantId, CancellationToken.None);

        Assert.Equal(descriptor, result);
    }

    [Fact]
    public async Task GetByIdAsync_UnseededTenant_ReturnsNull()
    {
        var catalog = new InMemoryTenantCatalog();

        var result = await catalog.GetByIdAsync(new TenantId(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_SeededWithResolutionKey_ReturnsDescriptor()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor(displayName: "Contoso");
        catalog.SeedTenant(descriptor, resolutionKey: "contoso.example.com");

        var result = await catalog.GetByResolutionKeyAsync("contoso.example.com", CancellationToken.None);

        Assert.Equal(descriptor, result);
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_UnseededKey_ReturnsNull()
    {
        var catalog = new InMemoryTenantCatalog();
        catalog.SeedTenant(CreateDescriptor(), resolutionKey: "known.example.com");

        var result = await catalog.GetByResolutionKeyAsync("unknown.example.com", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_SeededWithoutResolutionKey_NotIndexed()
    {
        var catalog = new InMemoryTenantCatalog();
        catalog.SeedTenant(CreateDescriptor(), resolutionKey: null);

        var result = await catalog.GetByResolutionKeyAsync("anything", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task MutateStatus_SeededTenant_UpdatesStatusInPlace()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor(status: TenantStatus.Active);
        catalog.SeedTenant(descriptor);

        catalog.MutateStatus(descriptor.TenantId, TenantStatus.Suspended);
        var result = await catalog.GetByIdAsync(descriptor.TenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TenantStatus.Suspended, result!.Status);
        // Every other field is preserved by the `with` expression.
        Assert.Equal(descriptor.DisplayName, result.DisplayName);
        Assert.Equal(descriptor.IsolationMode, result.IsolationMode);
    }

    [Fact]
    public void MutateStatus_UnseededTenant_IsNoOp()
    {
        var catalog = new InMemoryTenantCatalog();

        var exception = Record.Exception(() => catalog.MutateStatus(new TenantId(Guid.NewGuid()), TenantStatus.Offboarded));

        Assert.Null(exception);
    }

    [Fact]
    public async Task MutateStatus_AlsoUpdatesResolutionKeyLookup()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor(status: TenantStatus.Active);
        catalog.SeedTenant(descriptor, resolutionKey: "tenant.example.com");

        catalog.MutateStatus(descriptor.TenantId, TenantStatus.Offboarded);
        var result = await catalog.GetByResolutionKeyAsync("tenant.example.com", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TenantStatus.Offboarded, result!.Status);
    }

    [Fact]
    public async Task Reset_ClearsAllSeededTenantsAndResolutionKeys()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor();
        catalog.SeedTenant(descriptor, resolutionKey: "tenant.example.com");

        catalog.Reset();

        Assert.Null(await catalog.GetByIdAsync(descriptor.TenantId, CancellationToken.None));
        Assert.Null(await catalog.GetByResolutionKeyAsync("tenant.example.com", CancellationToken.None));
    }

    [Fact]
    public void SeedTenant_NullDescriptor_Throws()
    {
        var catalog = new InMemoryTenantCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.SeedTenant(null!));
    }

    [Fact]
    public async Task GetByResolutionKeyAsync_NullKey_Throws()
    {
        var catalog = new InMemoryTenantCatalog();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await catalog.GetByResolutionKeyAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ComposesWithRealCatalogTenantStatusValidator_ActiveTenant_ReturnsTrue()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor(status: TenantStatus.Active);
        catalog.SeedTenant(descriptor);
        var validator = new CatalogTenantStatusValidator(catalog);

        var isActive = await validator.IsActiveAsync(descriptor.TenantId, CancellationToken.None);

        Assert.True(isActive);
    }

    [Fact]
    public async Task ComposesWithRealCatalogTenantStatusValidator_SuspendedTenant_ReturnsFalse()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor(status: TenantStatus.Suspended);
        catalog.SeedTenant(descriptor);
        var validator = new CatalogTenantStatusValidator(catalog);

        var isActive = await validator.IsActiveAsync(descriptor.TenantId, CancellationToken.None);

        Assert.False(isActive);
    }

    [Fact]
    public async Task ComposesWithRealCatalogTenantStatusValidator_UnknownTenant_FailsClosed()
    {
        var catalog = new InMemoryTenantCatalog();
        var validator = new CatalogTenantStatusValidator(catalog);

        var isActive = await validator.IsActiveAsync(new TenantId(Guid.NewGuid()), CancellationToken.None);

        Assert.False(isActive);
    }

    [Fact]
    public async Task ComposesWithRealCatalogTenantStatusValidator_MutatedMidTest_ReflectsNewStatus()
    {
        var catalog = new InMemoryTenantCatalog();
        var descriptor = CreateDescriptor(status: TenantStatus.Active);
        catalog.SeedTenant(descriptor);
        var validator = new CatalogTenantStatusValidator(catalog);

        Assert.True(await validator.IsActiveAsync(descriptor.TenantId, CancellationToken.None));

        catalog.MutateStatus(descriptor.TenantId, TenantStatus.Offboarded);

        Assert.False(await validator.IsActiveAsync(descriptor.TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task SeedTenant_ReSeedingSameTenantId_OverwritesPreviousDescriptor()
    {
        var catalog = new InMemoryTenantCatalog();
        TenantId tenantId = new TenantId(Guid.NewGuid());
        catalog.SeedTenant(CreateDescriptor(tenantId, displayName: "Original"));
        catalog.SeedTenant(CreateDescriptor(tenantId, displayName: "Renamed"));

        var result = await catalog.GetByIdAsync(tenantId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Renamed", result!.DisplayName);
    }
}
