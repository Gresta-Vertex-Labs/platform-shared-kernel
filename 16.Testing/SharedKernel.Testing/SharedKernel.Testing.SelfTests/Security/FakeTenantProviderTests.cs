using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="FakeTenantProvider"/> against the <see cref="ITenantProvider"/> contract owned
/// by <c>12.Security</c>.
/// </summary>
/// <remarks>
/// Routed here per the D-52 fallback (same reasoning as <see cref="FakeUserContextTests"/>): no
/// consuming-domain test project references <c>SharedKernel.Testing</c> for these two types yet.
/// </remarks>
public sealed class FakeTenantProviderTests
{
    [Fact]
    public void Constructor_NoArgument_DefaultsToFixedNonEmptyGuid()
    {
        var provider = new FakeTenantProvider();

        Assert.NotEqual(Guid.Empty, provider.TenantId);
        Assert.Equal(new Guid("22222222-2222-2222-2222-222222222222"), provider.TenantId);
    }

    [Fact]
    public void Constructor_WithExplicitTenantId_UsesSuppliedValue()
    {
        var tenantId = Guid.NewGuid();
        var provider = new FakeTenantProvider(tenantId);

        Assert.Equal(tenantId, provider.TenantId);
    }

    [Fact]
    public void Constructor_WithExplicitGuidEmpty_OptsIntoNoTenantCase()
    {
        var provider = new FakeTenantProvider(Guid.Empty);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public void TenantId_CanBeMutatedAfterConstruction()
    {
        var provider = new FakeTenantProvider();
        var newTenantId = Guid.NewGuid();

        provider.TenantId = newTenantId;

        Assert.Equal(newTenantId, provider.TenantId);
    }

    [Fact]
    public void TenantId_CanBeMutatedToGuidEmpty_ToSimulateNoTenantResolvedPath()
    {
        var provider = new FakeTenantProvider();

        provider.TenantId = Guid.Empty;

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public void ImplementsITenantProvider()
    {
        ITenantProvider provider = new FakeTenantProvider();

        Assert.IsType<FakeTenantProvider>(provider);
    }
}
