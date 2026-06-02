using System.Security.Claims;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Mapping;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Mapping;

public sealed class OidcTenantProviderTests
{
    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void ValidTenantIdClaim_ParsedCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, tenantId.ToString()));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(tenantId, sut.TenantId);
    }

    [Fact]
    public void AbsentTenantIdClaim_ReturnsGuidEmpty()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Email, "user@example.com"));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void MalformedTenantIdClaim_ReturnsGuidEmpty()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, "not-a-guid"));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void EmptyTenantIdClaim_ReturnsGuidEmpty()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, string.Empty));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void EmptyPrincipal_ReturnsGuidEmpty()
    {
        var principal = new ClaimsPrincipal();

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void NullPrincipal_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OidcTenantProvider(null!));
    }
}
