using FluentAssertions;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.GroupNaming;

/// <summary>Design D12: tenant group names, and the B12 guard against one group for every tenantless connection.</summary>
public sealed class HubGroupNamingTests
{
    [Fact]
    public void TenantGroup_IsTheTenantPrefixAndTheDashedTenantId()
    {
        var tenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        HubGroupNaming.TenantGroup(tenantId).Should().Be("tenant:11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void B12_TenantGroup_RefusesGuidEmpty()
    {
        var build = () => HubGroupNaming.TenantGroup(Guid.Empty);

        build.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("tenantId");
    }
}
