using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Testing.ServiceDefaults;
using Xunit;

namespace SharedKernel.Testing.SelfTests.ServiceDefaults;

public sealed class StaticTenantProviderTests
{
    [Fact]
    public void TenantId_ReturnsFixedConstructorValue()
    {
        var tenantId = Guid.NewGuid();
        var provider = new StaticTenantProvider(tenantId);

        Assert.Equal(tenantId, provider.TenantId);
    }

    [Fact]
    public void TenantId_GuidEmpty_SimulatesNoTenantCase()
    {
        var provider = new StaticTenantProvider(Guid.Empty);

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public void ImplementsITenantProvider()
    {
        ITenantProvider provider = new StaticTenantProvider(Guid.NewGuid());

        Assert.IsType<StaticTenantProvider>(provider);
    }
}
