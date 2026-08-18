using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="SecurityTestContextBuilder"/>'s identity-basics surface (T-79) and its
/// AMR/ACR/AuthTime step-up-authentication surface (T-80).
/// </summary>
/// <remarks>
/// Routed here per the D-52 fallback (mirroring <see cref="FakeUserContextTests"/>): neither
/// <c>SharedKernel.Security.Abstractions.Tests.csproj</c> nor <c>SharedKernel.Security.Oidc.Tests.csproj</c>
/// carries a <c>ProjectReference</c> to <c>SharedKernel.Testing</c>, so there is no real consumer in the
/// owning domain to anchor against.
/// </remarks>
public sealed class SecurityTestContextBuilderTests
{
    // ----- T-79: identity basics -----

    [Fact]
    public void Build_Default_IsAuthenticatedTrue()
    {
        var principal = new SecurityTestContextBuilder().Build();

        Assert.True(principal.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Build_AfterUnauthenticated_IsAuthenticatedFalse()
    {
        var principal = new SecurityTestContextBuilder().Unauthenticated().Build();

        Assert.False(principal.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Build_Roles_EmitOneClaimPerRole_UnderRolesClaimType()
    {
        var principal = new SecurityTestContextBuilder().WithRoles("Admin", "Editor").Build();

        var roleClaims = principal.Claims.Where(c => c.Type == "roles").ToArray();

        Assert.Equal(2, roleClaims.Length);
        Assert.Contains(roleClaims, c => c.Value == "Admin");
        Assert.Contains(roleClaims, c => c.Value == "Editor");
    }

    [Fact]
    public void Build_Permissions_EmitOneSpaceDelimitedScopeClaim()
    {
        var principal = new SecurityTestContextBuilder()
            .WithPermissions("orders:write", "orders:read")
            .Build();

        var scopeClaims = principal.Claims.Where(c => c.Type == "scope").ToArray();

        Assert.Single(scopeClaims);
        Assert.Equal("orders:write orders:read", scopeClaims[0].Value);
    }

    [Fact]
    public void Build_NoPermissions_EmitsNoScopeClaim()
    {
        var principal = new SecurityTestContextBuilder().Build();

        Assert.DoesNotContain(principal.Claims, c => c.Type == "scope");
    }

    [Fact]
    public void Build_WithClaim_IsAdditive_AndAppearsVerbatim()
    {
        var principal = new SecurityTestContextBuilder()
            .WithClaim("custom-a", "value-a")
            .WithClaim("custom-b", "value-b")
            .Build();

        Assert.Contains(principal.Claims, c => c.Type == "custom-a" && c.Value == "value-a");
        Assert.Contains(principal.Claims, c => c.Type == "custom-b" && c.Value == "value-b");
    }

    [Fact]
    public void Build_WithClaims_IsBulkAdditive_AndAppearsVerbatim()
    {
        var principal = new SecurityTestContextBuilder()
            .WithClaim("custom-a", "value-a")
            .WithClaims(new Dictionary<string, string> { ["custom-b"] = "value-b", ["custom-c"] = "value-c" })
            .Build();

        Assert.Contains(principal.Claims, c => c.Type == "custom-a" && c.Value == "value-a");
        Assert.Contains(principal.Claims, c => c.Type == "custom-b" && c.Value == "value-b");
        Assert.Contains(principal.Claims, c => c.Type == "custom-c" && c.Value == "value-c");
    }

    [Fact]
    public void BuildUserContext_ReturnsFakeUserContext()
    {
        var context = new SecurityTestContextBuilder().BuildUserContext();

        Assert.IsType<FakeUserContext>(context);
    }

    [Fact]
    public void BuildUserContext_ProjectsEveryFluentField_Exactly()
    {
        var userId = Guid.NewGuid();

        var context = new SecurityTestContextBuilder()
            .WithUserId(userId)
            .WithEmail("user@test.com")
            .WithUsername("test-user")
            .WithRoles("Admin", "Editor")
            .WithPermissions("orders:write")
            .WithIdentityKind(IdentityKind.ServicePrincipal)
            .BuildUserContext();

        Assert.Equal(userId, context.UserId);
        Assert.Equal("user@test.com", context.Email);
        Assert.Equal("test-user", context.Username);
        Assert.Equal(2, context.Roles.Count);
        Assert.Contains("Admin", context.Roles);
        Assert.Contains("Editor", context.Roles);
        Assert.Single(context.Permissions);
        Assert.Contains("orders:write", context.Permissions);
        Assert.Equal(IdentityKind.ServicePrincipal, context.IdentityKind);
        Assert.True(context.IsAuthenticated);
    }

    [Fact]
    public void BuildUserContext_AfterUnauthenticated_IsAuthenticatedFalse()
    {
        var context = new SecurityTestContextBuilder().Unauthenticated().BuildUserContext();

        Assert.False(context.IsAuthenticated);
    }

    [Fact]
    public void BuildUserContext_WithClaim_AppearsVerbatimInClaimsDictionary()
    {
        var context = new SecurityTestContextBuilder()
            .WithClaim("custom-a", "value-a")
            .WithClaims(new Dictionary<string, string> { ["custom-b"] = "value-b" })
            .BuildUserContext();

        Assert.Equal("value-a", context.Claims["custom-a"]);
        Assert.Equal("value-b", context.Claims["custom-b"]);
    }

    [Fact]
    public void BuildUserContext_ClaimsDictionary_NeverDerivedFromClaimsPrincipal_ContainsOnlyWithClaimEntries()
    {
        // Anchor "independence" proof: if BuildUserContext() were reimplemented by constructing a
        // ClaimsPrincipal via Build() and parsing it back (e.g. dumping every claim into a dictionary),
        // the standard identity claim types below ("sub"/"email"/"name"/"roles"/"scope") would leak
        // into .Claims. They must not — only WithClaim/WithClaims entries ever appear there.
        var context = new SecurityTestContextBuilder()
            .WithUserId(Guid.NewGuid())
            .WithEmail("user@test.com")
            .WithUsername("test-user")
            .WithRoles("Admin")
            .WithPermissions("orders:write")
            .WithClaim("custom", "value")
            .BuildUserContext();

        Assert.Single(context.Claims);
        Assert.Equal("value", context.Claims["custom"]);
        Assert.False(context.Claims.ContainsKey("sub"));
        Assert.False(context.Claims.ContainsKey("email"));
        Assert.False(context.Claims.ContainsKey("name"));
        Assert.False(context.Claims.ContainsKey("roles"));
        Assert.False(context.Claims.ContainsKey("scope"));
    }

    [Fact]
    public void BuildUserContext_StandardFields_ComeFromDedicatedFluentState_NotFromAWithClaimOverride()
    {
        // A second independence proof: seed a WithClaim under the SAME claim type Build() uses for
        // Email ("email"), with a DIFFERENT value than .WithEmail(...). A naive reimplementation that
        // derived BuildUserContext() by parsing the ClaimsPrincipal's "email" claim could still
        // accidentally pass this if it picked the right claim — but proves the dedicated FakeUserContext
        // field, never the principal, is the actual source of truth for the standard identity members,
        // while the WithClaim-injected value still surfaces verbatim in .Claims under its own key.
        var context = new SecurityTestContextBuilder()
            .WithEmail("dedicated@test.com")
            .WithClaim("email", "from-with-claim@test.com")
            .BuildUserContext();

        Assert.Equal("dedicated@test.com", context.Email);
        Assert.Equal("from-with-claim@test.com", context.Claims["email"]);
    }

    // ----- T-80: AMR/ACR/AuthTime -----

    [Fact]
    public void Build_AuthenticationMethods_EmitDiscretePerMethodClaims_NeverSpaceDelimited()
    {
        var principal = new SecurityTestContextBuilder()
            .WithAuthenticationMethods("pwd", "otp")
            .Build();

        var amrClaims = principal.Claims.Where(c => c.Type == "amr").ToArray();

        Assert.Equal(2, amrClaims.Length);
        Assert.Contains(amrClaims, c => c.Value == "pwd");
        Assert.Contains(amrClaims, c => c.Value == "otp");
        Assert.DoesNotContain(amrClaims, c => c.Value.Contains(' '));
    }

    [Fact]
    public void Build_AuthContextClassReference_EmitsAcrClaim()
    {
        var principal = new SecurityTestContextBuilder()
            .WithAuthContextClassReference("urn:mace:incommon:iap:silver")
            .Build();

        Assert.Contains(principal.Claims, c => c.Type == "acr" && c.Value == "urn:mace:incommon:iap:silver");
    }

    [Fact]
    public void Build_AuthTime_EmitsAuthTimeClaim_AsUnixSecondsNumericDate()
    {
        var authTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var principal = new SecurityTestContextBuilder().WithAuthTime(authTime).Build();

        var claim = Assert.Single(principal.Claims, c => c.Type == "auth_time");
        Assert.Equal(
            authTime.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            claim.Value);
    }

    [Fact]
    public void Build_NoAuthTime_EmitsNoAuthTimeClaim()
    {
        var principal = new SecurityTestContextBuilder().Build();

        Assert.DoesNotContain(principal.Claims, c => c.Type == "auth_time");
    }

    [Fact]
    public void BuildUserContext_ProjectsAmrAcrAuthTime_Exactly()
    {
        var authTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var context = new SecurityTestContextBuilder()
            .WithAuthenticationMethods("pwd", "otp")
            .WithAuthContextClassReference("urn:test:acr")
            .WithAuthTime(authTime)
            .BuildUserContext();

        Assert.Equal(2, context.AuthenticationMethods.Count);
        Assert.Contains("pwd", context.AuthenticationMethods);
        Assert.Contains("otp", context.AuthenticationMethods);
        Assert.Equal("urn:test:acr", context.AuthContextClassReference);
        Assert.Equal(authTime, context.AuthTime);
    }

    [Fact]
    public void BuildUserContext_Defaults_AuthenticationMethodsEmpty_AcrNull_AuthTimeNull()
    {
        var context = new SecurityTestContextBuilder().BuildUserContext();

        Assert.Empty(context.AuthenticationMethods);
        Assert.Null(context.AuthContextClassReference);
        Assert.Null(context.AuthTime);
    }

    [Fact]
    public void WasAuthenticatedWith_MatchesCaseInsensitively_AgainstMultiValueAuthenticationMethods()
    {
        var context = new SecurityTestContextBuilder()
            .WithAuthenticationMethods("pwd", "otp")
            .BuildUserContext();

        Assert.True(context.WasAuthenticatedWith("PWD"));
        Assert.True(context.WasAuthenticatedWith("Otp"));
        Assert.False(context.WasAuthenticatedWith("mfa"));
    }

    [Fact]
    public void IsAuthenticationFresherThan_ExplicitNow_WithinMaxAge_ReturnsTrue()
    {
        var authTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = authTime.AddMinutes(4);
        var context = new SecurityTestContextBuilder().WithAuthTime(authTime).BuildUserContext();

        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_ExplicitNow_BeyondMaxAge_ReturnsFalse()
    {
        var authTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var now = authTime.AddMinutes(6);
        var context = new SecurityTestContextBuilder().WithAuthTime(authTime).BuildUserContext();

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_NullAuthTime_AlwaysReturnsFalse()
    {
        var context = new SecurityTestContextBuilder().BuildUserContext();
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.FromDays(365), now));
    }
}
