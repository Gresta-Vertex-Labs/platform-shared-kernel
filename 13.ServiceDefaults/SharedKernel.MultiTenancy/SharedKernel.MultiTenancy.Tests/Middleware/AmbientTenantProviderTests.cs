using System.Reflection;
using SharedKernel.MultiTenancy.Middleware;

namespace SharedKernel.MultiTenancy.Tests.Middleware;

public sealed class AmbientTenantProviderTests
{
    [Fact]
    public void TenantId_DefaultsTo_GuidEmpty()
    {
        var provider = new AmbientTenantProvider();

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public void TenantId_SetterIsPrivate()
    {
        var property = typeof(AmbientTenantProvider).GetProperty(nameof(AmbientTenantProvider.TenantId));

        Assert.NotNull(property);
        var setMethod = property!.GetSetMethod(nonPublic: true);
        Assert.NotNull(setMethod);
        Assert.True(setMethod!.IsPrivate);
    }

    [Fact]
    public void SetTenantId_UpdatesTenantId()
    {
        var provider = new AmbientTenantProvider();
        var tenantId = Guid.NewGuid();

        var method = typeof(AmbientTenantProvider).GetMethod(
            "SetTenantId",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method!.Invoke(provider, [tenantId]);

        Assert.Equal(tenantId, provider.TenantId);
    }
}
