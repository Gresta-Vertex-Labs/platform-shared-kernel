using System.Security.Claims;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

public sealed class ApiKeyUserContextTests
{
    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, ApiKeyAuthenticationOptionsScheme));

    private const string ApiKeyAuthenticationOptionsScheme = "ApiKey";

    [Fact]
    public void IsAuthenticated_IsAlwaysTrue()
    {
        var sut = new ApiKeyUserContext(BuildPrincipal());
        Assert.True(sut.IsAuthenticated);
    }

    [Fact]
    public void IdentityKind_IsServicePrincipal()
    {
        var sut = new ApiKeyUserContext(BuildPrincipal());
        Assert.Equal(IdentityKind.ServicePrincipal, sut.IdentityKind);
    }

    [Fact]
    public void UserId_IsAlwaysGuidEmpty()
    {
        var sut = new ApiKeyUserContext(BuildPrincipal());
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void Username_MappedFromClientIdClaim()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.NameIdentifier, "client-42"));
        var sut = new ApiKeyUserContext(principal);

        Assert.Equal("client-42", sut.Username);
    }

    [Fact]
    public void Username_IsNull_WhenClientIdClaimAbsent()
    {
        var sut = new ApiKeyUserContext(BuildPrincipal());
        Assert.Null(sut.Username);
    }

    [Fact]
    public void Email_IsAlwaysNull()
    {
        var sut = new ApiKeyUserContext(BuildPrincipal());
        Assert.Null(sut.Email);
    }

    [Fact]
    public void Roles_CollectedFromRoleClaims()
    {
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.Role, "admin"),
            new Claim(SecurityClaimTypes.Role, "editor"));

        var sut = new ApiKeyUserContext(principal);

        Assert.Equal(2, sut.Roles.Count);
        Assert.Contains("admin", sut.Roles);
        Assert.Contains("editor", sut.Roles);
    }

    [Fact]
    public void HasRole_IsCaseInsensitive()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.Role, "Admin"));
        var sut = new ApiKeyUserContext(principal);

        Assert.True(sut.HasRole("admin"));
        Assert.True(sut.HasRole("ADMIN"));
        Assert.False(sut.HasRole("editor"));
    }

    [Fact]
    public void Permissions_CollectedFromInternalPermissionClaim()
    {
        var principal = BuildPrincipal(
            new Claim("permission", "orders:read"),
            new Claim("permission", "orders:write"));

        var sut = new ApiKeyUserContext(principal);

        Assert.Equal(2, sut.Permissions.Count);
        Assert.Contains("orders:read", sut.Permissions);
        Assert.Contains("orders:write", sut.Permissions);
    }

    [Fact]
    public void HasPermission_IsCaseInsensitive()
    {
        var principal = BuildPrincipal(new Claim("permission", "Orders:Read"));
        var sut = new ApiKeyUserContext(principal);

        Assert.True(sut.HasPermission("orders:read"));
        Assert.True(sut.HasPermission("ORDERS:READ"));
        Assert.False(sut.HasPermission("orders:write"));
    }

    [Fact]
    public void Claims_FirstValueWins_ForMultiValueClaims()
    {
        var principal = BuildPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "first"),
            new Claim(ClaimTypes.NameIdentifier, "second"));

        var sut = new ApiKeyUserContext(principal);

        Assert.Equal("first", sut.Claims[ClaimTypes.NameIdentifier]);
    }

    [Fact]
    public void NullPrincipal_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ApiKeyUserContext(null!));
    }

    [Fact]
    public void ImplementsIUserContext()
    {
        var sut = new ApiKeyUserContext(BuildPrincipal());
        Assert.IsAssignableFrom<IUserContext>(sut);
    }
}
