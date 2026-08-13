using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="FakeUserContext"/> against the <see cref="IUserContext"/> contract owned by
/// <c>12.Security</c>.
/// </summary>
/// <remarks>
/// Routed here per the D-52 fallback: neither <c>SharedKernel.Security.Abstractions.Tests.csproj</c>
/// nor <c>SharedKernel.Security.Oidc.Tests.csproj</c> carries a <c>ProjectReference</c> to
/// <c>SharedKernel.Testing</c>, so there is no real consumer in the owning domain to anchor against —
/// the same fallback already applied to <c>StaticTenantProvider</c> and <c>FakeClock</c>.
/// </remarks>
public sealed class FakeUserContextTests
{
    [Fact]
    public void Constructor_Defaults_IsAuthenticatedTrue()
    {
        var context = new FakeUserContext();

        Assert.True(context.IsAuthenticated);
    }

    [Fact]
    public void Constructor_Defaults_UserIdIsFixedNonEmptyGuid()
    {
        var context = new FakeUserContext();

        Assert.NotEqual(Guid.Empty, context.UserId);
        Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), context.UserId);
    }

    [Fact]
    public void Constructor_Defaults_RolesAndClaimsAreEmpty()
    {
        var context = new FakeUserContext();

        Assert.Empty(context.Roles);
        Assert.Empty(context.Claims);
    }

    [Fact]
    public void Constructor_Defaults_EmailAndUsernameAreNull()
    {
        var context = new FakeUserContext();

        Assert.Null(context.Email);
        Assert.Null(context.Username);
    }

    [Fact]
    public void IsAuthenticated_CanBeSetFalse_ToExerciseUnauthenticatedPath()
    {
        var context = new FakeUserContext { IsAuthenticated = false };

        Assert.False(context.IsAuthenticated);
    }

    [Fact]
    public void Roles_CanBeSet_ToExerciseRoleRestrictedPath()
    {
        var context = new FakeUserContext { Roles = ["Admin", "Editor"] };

        Assert.Equal(2, context.Roles.Count);
        Assert.Contains("Admin", context.Roles);
    }

    [Fact]
    public void HasRole_MatchingRole_ReturnsTrue()
    {
        var context = new FakeUserContext { Roles = ["Admin"] };

        Assert.True(context.HasRole("Admin"));
    }

    [Fact]
    public void HasRole_IsCaseInsensitive()
    {
        var context = new FakeUserContext { Roles = ["Admin"] };

        Assert.True(context.HasRole("admin"));
        Assert.True(context.HasRole("ADMIN"));
    }

    [Fact]
    public void HasRole_NoMatch_ReturnsFalse()
    {
        var context = new FakeUserContext { Roles = ["Admin"] };

        Assert.False(context.HasRole("Viewer"));
    }

    [Fact]
    public void HasRole_EmptyRoles_ReturnsFalse()
    {
        var context = new FakeUserContext();

        Assert.False(context.HasRole("Admin"));
    }

    [Fact]
    public void UserId_CanBeOverridden()
    {
        var userId = Guid.NewGuid();
        var context = new FakeUserContext { UserId = userId };

        Assert.Equal(userId, context.UserId);
    }

    [Fact]
    public void Claims_CanBeSet()
    {
        var context = new FakeUserContext
        {
            Claims = new Dictionary<string, string> { ["sub"] = "test-user" },
        };

        Assert.Equal("test-user", context.Claims["sub"]);
    }

    [Fact]
    public void ImplementsIUserContext()
    {
        IUserContext context = new FakeUserContext();

        Assert.IsType<FakeUserContext>(context);
    }

    [Fact]
    public void Constructor_Defaults_IdentityKindIsUser()
    {
        var context = new FakeUserContext();

        Assert.Equal(IdentityKind.User, context.IdentityKind);
    }

    [Theory]
    [InlineData(IdentityKind.Anonymous)]
    [InlineData(IdentityKind.ServicePrincipal)]
    [InlineData(IdentityKind.System)]
    [InlineData(IdentityKind.User)]
    public void IdentityKind_CanBeSet_ToExerciseEveryValue(IdentityKind identityKind)
    {
        var context = new FakeUserContext { IdentityKind = identityKind };

        Assert.Equal(identityKind, context.IdentityKind);
    }

    [Fact]
    public void Constructor_Defaults_PermissionsIsEmpty()
    {
        var context = new FakeUserContext();

        Assert.Empty(context.Permissions);
    }

    [Fact]
    public void Permissions_CanBeSet_ToExercisePermissionRestrictedPath()
    {
        var context = new FakeUserContext { Permissions = ["orders:write", "orders:read"] };

        Assert.Equal(2, context.Permissions.Count);
        Assert.Contains("orders:write", context.Permissions);
    }

    [Fact]
    public void HasPermission_MatchingPermission_ReturnsTrue()
    {
        var context = new FakeUserContext { Permissions = ["orders:write"] };

        Assert.True(context.HasPermission("orders:write"));
    }

    [Fact]
    public void HasPermission_IsCaseInsensitive()
    {
        var context = new FakeUserContext { Permissions = ["orders:write"] };

        Assert.True(context.HasPermission("ORDERS:WRITE"));
        Assert.True(context.HasPermission("Orders:Write"));
    }

    [Fact]
    public void HasPermission_NoMatch_ReturnsFalse()
    {
        var context = new FakeUserContext { Permissions = ["orders:write"] };

        Assert.False(context.HasPermission("orders:delete"));
    }

    [Fact]
    public void HasPermission_EmptyPermissions_ReturnsFalse()
    {
        var context = new FakeUserContext();

        Assert.False(context.HasPermission("orders:write"));
    }

    [Theory]
    [InlineData("orders:write")]
    [InlineData("")]
    [InlineData("anything")]
    public void HasPermission_AbsentPermissions_NeverThrows_AlwaysReturnsFalse(string permission)
    {
        var context = new FakeUserContext();

        var result = Record.Exception(() => context.HasPermission(permission));

        Assert.Null(result);
        Assert.False(context.HasPermission(permission));
    }
}
