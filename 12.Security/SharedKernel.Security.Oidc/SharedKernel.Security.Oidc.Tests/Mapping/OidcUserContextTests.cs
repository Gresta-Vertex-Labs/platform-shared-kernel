using System.Security.Claims;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Mapping;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Mapping;

public sealed class OidcUserContextTests
{
    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UnauthenticatedPrincipal(params Claim[] claims)
    {
        // No authentication type → IsAuthenticated = false
        var identity = new ClaimsIdentity(claims);
        return new ClaimsPrincipal(identity);
    }

    // ---- UserId and IsAuthenticated ----

    [Fact]
    public void ValidPrincipal_MapsUserId_AndIsAuthenticated()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal);

        Assert.Equal(userId, sut.UserId);
        Assert.True(sut.IsAuthenticated);
    }

    [Fact]
    public void MissingSubClaim_ForcesIsAuthenticated_False()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Email, "user@example.com"));

        var sut = new OidcUserContext(principal);

        Assert.False(sut.IsAuthenticated);
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void UnparseableSubClaim_ForcesIsAuthenticated_False()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, "not-a-guid"));

        var sut = new OidcUserContext(principal);

        Assert.False(sut.IsAuthenticated);
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void UnauthenticatedPrincipal_WithValidSub_IsAuthenticated_False()
    {
        // ClaimsIdentity without authentication type → IsAuthenticated = false
        var userId = Guid.NewGuid();
        var principal = UnauthenticatedPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal);

        Assert.False(sut.IsAuthenticated);
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    // ---- Email and Username ----

    [Fact]
    public void Email_MappedFromEmailClaim()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(SecurityClaimTypes.Email, "test@example.com"));

        var sut = new OidcUserContext(principal);

        Assert.Equal("test@example.com", sut.Email);
    }

    [Fact]
    public void Email_IsNull_WhenClaimAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal);

        Assert.Null(sut.Email);
    }

    [Fact]
    public void Username_MappedFromNameClaim()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(ClaimTypes.Name, "jdoe"));

        var sut = new OidcUserContext(principal);

        Assert.Equal("jdoe", sut.Username);
    }

    [Fact]
    public void Username_IsNull_WhenClaimAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal);

        Assert.Null(sut.Username);
    }

    // ---- Roles ----

    [Fact]
    public void Roles_CollectedFromRoleClaims()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(SecurityClaimTypes.Role, "admin"),
            new Claim(SecurityClaimTypes.Role, "editor"));

        var sut = new OidcUserContext(principal);

        Assert.Contains("admin", sut.Roles);
        Assert.Contains("editor", sut.Roles);
        Assert.Equal(2, sut.Roles.Count);
    }

    [Fact]
    public void Roles_IsEmpty_WhenNoRoleClaims()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal);

        Assert.Empty(sut.Roles);
    }

    // ---- HasRole (case-insensitivity) ----

    [Fact]
    public void HasRole_ReturnsTrueForExactCaseMatch()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(SecurityClaimTypes.Role, "Admin"));

        var sut = new OidcUserContext(principal);

        Assert.True(sut.HasRole("Admin"));
    }

    [Fact]
    public void HasRole_ReturnsTrueForDifferentCaseMatch()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(SecurityClaimTypes.Role, "Admin"));

        var sut = new OidcUserContext(principal);

        Assert.True(sut.HasRole("admin"));
        Assert.True(sut.HasRole("ADMIN"));
        Assert.True(sut.HasRole("aDmIn"));
    }

    [Fact]
    public void HasRole_ReturnsFalseForAbsentRole()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(SecurityClaimTypes.Role, "editor"));

        var sut = new OidcUserContext(principal);

        Assert.False(sut.HasRole("admin"));
    }

    // ---- Claims dictionary ----

    [Fact]
    public void Claims_FirstValueWins_ForMultiValueClaims()
    {
        var userId = Guid.NewGuid();
        // ClaimTypes.Email appears twice — first value wins
        var identity = new ClaimsIdentity(
        [
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(ClaimTypes.Email, "first@example.com"),
            new Claim(ClaimTypes.Email, "second@example.com"),
        ], "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var sut = new OidcUserContext(principal);

        Assert.Equal("first@example.com", sut.Claims[ClaimTypes.Email]);
    }

    [Fact]
    public void Claims_IsEmpty_WhenNoClaims()
    {
        var identity = new ClaimsIdentity([], "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var sut = new OidcUserContext(principal);

        Assert.Empty(sut.Claims);
    }
}
