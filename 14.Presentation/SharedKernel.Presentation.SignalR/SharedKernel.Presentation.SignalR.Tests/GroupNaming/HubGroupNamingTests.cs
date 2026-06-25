using FluentAssertions;
using SharedKernel.Presentation.SignalR.GroupNaming;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.GroupNaming;

public class HubGroupNamingTests
{
    [Fact]
    public void TenantGroup_FormatsAsTenantPrefixWithDFormatGuid()
    {
        var tenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var groupName = HubGroupNaming.TenantGroup(tenantId);

        groupName.Should().Be("tenant:11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void TenantGroup_EmptyGuid_StillFormatsConsistently()
    {
        var groupName = HubGroupNaming.TenantGroup(Guid.Empty);

        groupName.Should().Be("tenant:00000000-0000-0000-0000-000000000000");
    }
}
