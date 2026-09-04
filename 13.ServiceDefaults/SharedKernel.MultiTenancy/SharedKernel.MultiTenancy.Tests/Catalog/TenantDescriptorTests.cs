using SharedKernel.MultiTenancy.Catalog;

namespace SharedKernel.MultiTenancy.Tests.Catalog;

public sealed class TenantDescriptorTests
{
    [Fact]
    public void RecordEquality_WithSameValues_AreEqual()
    {
        var tenantId = Guid.NewGuid();
        var settings = new Dictionary<string, string> { ["theme"] = "dark" };

        var first = new TenantDescriptor(tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, "en-US", settings);
        var second = new TenantDescriptor(tenantId, "Acme", TenantStatus.Active, TenantIsolationMode.Shared, "en-US", settings);

        Assert.Equal(first, second);
    }

    [Fact]
    public void WithExpression_ChangesOnlySpecifiedMember()
    {
        var tenantId = Guid.NewGuid();
        var original = new TenantDescriptor(
            tenantId,
            "Acme",
            TenantStatus.Active,
            TenantIsolationMode.Shared,
            null,
            new Dictionary<string, string>());

        var suspended = original with { Status = TenantStatus.Suspended };

        Assert.Equal(TenantStatus.Suspended, suspended.Status);
        Assert.Equal(original.TenantId, suspended.TenantId);
        Assert.Equal(original.DisplayName, suspended.DisplayName);
    }

    [Fact]
    public void DefaultCultureAndSettings_DefaultToNullAndEmpty_WithoutThrowing()
    {
        var descriptor = new TenantDescriptor(
            Guid.NewGuid(),
            "Acme",
            TenantStatus.Active,
            TenantIsolationMode.Dedicated,
            DefaultCulture: null,
            Settings: new Dictionary<string, string>());

        Assert.Null(descriptor.DefaultCulture);
        Assert.Empty(descriptor.Settings);
    }
}

/// <summary>
/// A minimal in-file test double proving <see cref="ITenantCatalog"/> is satisfiable by a trivial
/// implementation with no dependency on any concrete platform type.
/// </summary>
internal sealed class InMemoryTenantCatalogDouble : ITenantCatalog
{
    private readonly Dictionary<Guid, TenantDescriptor> _byId = [];

    public void Seed(TenantDescriptor descriptor) => _byId[descriptor.TenantId] = descriptor;

    public Task<TenantDescriptor?> GetByIdAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(_byId.GetValueOrDefault(tenantId));

    public Task<TenantDescriptor?> GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct) =>
        Task.FromResult<TenantDescriptor?>(null);
}

public sealed class ITenantCatalogContractTests
{
    [Fact]
    public async Task InMemoryDouble_SatisfiesInterface_AndRoundTripsSeededTenant()
    {
        var tenantId = Guid.NewGuid();
        var descriptor = new TenantDescriptor(
            tenantId,
            "Acme",
            TenantStatus.Active,
            TenantIsolationMode.Shared,
            "en-US",
            new Dictionary<string, string>());

        ITenantCatalog catalog = new InMemoryTenantCatalogDouble();
        ((InMemoryTenantCatalogDouble)catalog).Seed(descriptor);

        var resolved = await catalog.GetByIdAsync(tenantId, CancellationToken.None);

        Assert.Equal(descriptor, resolved);
    }
}
