using System.Security.Claims;
using SharedKernel.Security.Abstractions.Claims;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class SecurityClaimTypesTests
{
    [Fact]
    public void UserId_IsSubClaim()
    {
        Assert.Equal("sub", SecurityClaimTypes.UserId);
    }

    [Fact]
    public void TenantId_IsTenantIdClaim()
    {
        Assert.Equal("tenant_id", SecurityClaimTypes.TenantId);
    }

    [Fact]
    public void Email_MatchesClaimTypesEmail()
    {
        Assert.Equal(ClaimTypes.Email, SecurityClaimTypes.Email);
    }

    [Fact]
    public void Role_MatchesClaimTypesRole()
    {
        Assert.Equal(ClaimTypes.Role, SecurityClaimTypes.Role);
    }
}
