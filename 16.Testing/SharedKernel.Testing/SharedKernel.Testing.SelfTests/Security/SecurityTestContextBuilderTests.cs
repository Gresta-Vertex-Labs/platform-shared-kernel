using System.Globalization;
using System.Security.Claims;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

public sealed class SecurityTestContextBuilderTests
{
    private static readonly Guid TenantId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private static readonly DateTimeOffset AuthTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_Default_AuthenticatedBearerIdentityWithDefaultSubject()
    {
        var principal = new SecurityTestContextBuilder().Build();

        var identity = Assert.Single(principal.Identities);
        Assert.True(identity.IsAuthenticated);
        Assert.Equal("Bearer", identity.AuthenticationType);
        Assert.Equal(FakeUserContext.DefaultSubjectId, identity.FindFirst("sub")!.Value);
    }

    [Fact]
    public void Build_Default_EmitsOnlySubjectClaim()
    {
        var principal = new SecurityTestContextBuilder().Build();

        var claim = Assert.Single(principal.Claims);
        Assert.Equal(SecurityClaimTypes.Subject, claim.Type);
    }

    [Fact]
    public void Build_IdentityNameAndRoleClaimTypes_AreShortNames()
    {
        var principal = new SecurityTestContextBuilder().WithName("Ada").WithRoles("admin").Build();

        var identity = (ClaimsIdentity)principal.Identity!;
        Assert.Equal("name", identity.NameClaimType);
        Assert.Equal("roles", identity.RoleClaimType);
        Assert.Equal("Ada", identity.Name);
        Assert.True(principal.IsInRole("admin"));
    }

    [Fact]
    public void Build_Unauthenticated_IdentityHasNoAuthenticationType()
    {
        var principal = new SecurityTestContextBuilder().Unauthenticated().Build();

        Assert.False(principal.Identity!.IsAuthenticated);
        Assert.Null(principal.Identity.AuthenticationType);
    }

    [Fact]
    public void Build_WithIdentityKindAnonymous_SameAsUnauthenticated()
    {
        var principal = new SecurityTestContextBuilder().WithIdentityKind(IdentityKind.Anonymous).Build();

        Assert.False(principal.Identity!.IsAuthenticated);
    }

    [Fact]
    public void Build_AllFields_EmitsShortNameClaims()
    {
        var principal = new SecurityTestContextBuilder()
            .WithSubjectId("user-42")
            .WithClientId("spa-client")
            .WithTenantId(TenantId)
            .WithSessionId("session-7")
            .WithName("Ada")
            .WithEmail("ada@example.test")
            .WithAuthContextClassReference("urn:acr:silver")
            .WithAuthTime(AuthTime)
            .Build();

        Assert.Equal("user-42", Single(principal, "sub"));
        Assert.Equal("spa-client", Single(principal, "azp"));
        Assert.Equal("3f2504e0-4f89-41d3-9a0c-0305e82c3301", Single(principal, "tenant_id"));
        Assert.Equal("session-7", Single(principal, "sid"));
        Assert.Equal("Ada", Single(principal, "name"));
        Assert.Equal("ada@example.test", Single(principal, "email"));
        Assert.Equal("urn:acr:silver", Single(principal, "acr"));
        Assert.Equal(AuthTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), Single(principal, "auth_time"));
    }

    [Fact]
    public void Build_NullOptionalFields_EmitNoClaims()
    {
        var principal = new SecurityTestContextBuilder()
            .WithClientId("c").WithClientId(null)
            .WithTenantId(TenantId).WithTenantId(null)
            .WithSessionId("s").WithSessionId(null)
            .WithName("n").WithName(null)
            .WithEmail("e").WithEmail(null)
            .WithAuthContextClassReference("a").WithAuthContextClassReference(null)
            .WithAuthTime(AuthTime).WithAuthTime(null)
            .Build();

        Assert.Equal(["sub"], principal.Claims.Select(claim => claim.Type));
    }

    [Fact]
    public void Build_Roles_OneClaimPerRoleInOrder()
    {
        var principal = new SecurityTestContextBuilder().WithRoles("Admin", "Editor").Build();

        Assert.Equal(["Admin", "Editor"], principal.FindAll("roles").Select(claim => claim.Value));
    }

    [Fact]
    public void Build_WithRolesCalledTwice_ReplacesRoles()
    {
        var principal = new SecurityTestContextBuilder().WithRoles("Admin").WithRoles("Viewer").Build();

        Assert.Equal(["Viewer"], principal.FindAll("roles").Select(claim => claim.Value));
    }

    [Fact]
    public void Build_Permissions_OneSpaceDelimitedScopeClaim()
    {
        var principal = new SecurityTestContextBuilder().WithPermissions("orders:write", "orders:read").Build();

        Assert.Equal("orders:write orders:read", Single(principal, "scope"));
    }

    [Fact]
    public void Build_NoPermissions_EmitsNoScopeClaim()
    {
        var principal = new SecurityTestContextBuilder().WithPermissions("a").WithPermissions().Build();

        Assert.Empty(principal.FindAll("scope"));
    }

    [Fact]
    public void Build_WithAuthenticationMethodTime_EmitsTheClaimOfTheSharedHelper()
    {
        var verifiedAt = AuthTime.AddMinutes(30);

        var principal = new SecurityTestContextBuilder()
            .WithAuthenticationMethods("pwd", "otp")
            .WithAuthenticationMethodTime("otp", verifiedAt)
            .Build();

        var expected = AuthenticationMethodTimeClaim.Create("otp", verifiedAt);
        Assert.Equal(expected.Value, Single(principal, expected.Type));
        Assert.Equal(verifiedAt, AuthenticationMethodTimeClaim.Read(principal.Claims)["otp"]);
    }

    [Fact]
    public void Build_WithAuthenticationMethodTimeTwiceForOneMethod_EmitsOneClaim_WithTheLaterCall()
    {
        var principal = new SecurityTestContextBuilder()
            .WithAuthenticationMethodTime("otp", AuthTime)
            .WithAuthenticationMethodTime("hwk", AuthTime.AddMinutes(1))
            .WithAuthenticationMethodTime("otp", AuthTime.AddMinutes(2))
            .Build();

        var times = AuthenticationMethodTimeClaim.Read(principal.Claims);
        Assert.Equal(2, principal.FindAll(SecurityClaimTypes.AuthenticationMethodTime).Count());
        Assert.Equal(AuthTime.AddMinutes(2), times["otp"]);
        Assert.Equal(AuthTime.AddMinutes(1), times["hwk"]);
    }

    [Fact]
    public void WithAuthenticationMethodTime_SubSecondTime_ClaimRoundsDown_UserContextKeepsIt()
    {
        var verifiedAt = AuthTime.AddMilliseconds(900);
        var builder = new SecurityTestContextBuilder().WithAuthenticationMethods("otp").WithAuthenticationMethodTime("otp", verifiedAt);

        Assert.Equal(AuthTime, AuthenticationMethodTimeClaim.Read(builder.Build().Claims)["otp"]);
        Assert.Equal(verifiedAt, builder.BuildUserContext().GetAuthenticationMethodTime("otp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithAuthenticationMethodTime_NullOrWhitespaceMethod_Throws(string? method)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SecurityTestContextBuilder().WithAuthenticationMethodTime(method!, AuthTime));
    }

    [Fact]
    public void WithAuthenticationMethodTime_BeforeTheUnixEpoch_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SecurityTestContextBuilder().WithAuthenticationMethodTime("otp", DateTimeOffset.UnixEpoch.AddSeconds(-1)));
    }

    [Fact]
    public void BuildUserContext_WithAuthenticationMethodTime_ProjectsTheTimes()
    {
        var context = new SecurityTestContextBuilder()
            .WithAuthenticationMethods("pwd", "otp")
            .WithAuthTime(AuthTime)
            .WithAuthenticationMethodTime("otp", AuthTime.AddMinutes(30))
            .BuildUserContext();

        Assert.Equal(AuthTime.AddMinutes(30), Assert.Single(context.AuthenticationMethodTimes).Value);
        Assert.Equal(AuthTime.AddMinutes(30), context.GetAuthenticationMethodTime("otp"));
        Assert.Equal(AuthTime, context.GetAuthenticationMethodTime("pwd"));
        Assert.Equal(
            AuthenticationMethodTimeClaim.Create("otp", AuthTime.AddMinutes(30)).Value,
            context.FindClaim(SecurityClaimTypes.AuthenticationMethodTime));
    }

    [Fact]
    public void Build_AuthenticationMethods_OneClaimPerMethod()
    {
        var principal = new SecurityTestContextBuilder().WithAuthenticationMethods("pwd", "otp").Build();

        Assert.Equal(["pwd", "otp"], principal.FindAll("amr").Select(claim => claim.Value));
    }

    [Fact]
    public void Build_WithClaim_AppendedVerbatimInOrder()
    {
        var principal = new SecurityTestContextBuilder()
            .WithClaim("groups", "b")
            .WithClaim("groups", "a")
            .Build();

        Assert.Equal(["b", "a"], principal.FindAll("groups").Select(claim => claim.Value));
    }

    [Fact]
    public void Build_ServicePrincipal_EmitsIdtypApp()
    {
        var principal = new SecurityTestContextBuilder().WithIdentityKind(IdentityKind.ServicePrincipal).Build();

        Assert.True(principal.HasClaim("idtyp", "app"));
        Assert.True(principal.Identity!.IsAuthenticated);
    }

    [Theory]
    [InlineData(IdentityKind.User)]
    [InlineData(IdentityKind.Anonymous)]
    public void Build_NotServicePrincipal_EmitsNoIdtyp(IdentityKind kind)
    {
        var principal = new SecurityTestContextBuilder().WithIdentityKind(kind).Build();

        Assert.Empty(principal.FindAll("idtyp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithSubjectId_NullOrWhitespace_Throws(string? subjectId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SecurityTestContextBuilder().WithSubjectId(subjectId!));
    }

    [Fact]
    public void WithClaim_NullTypeOrValue_ThrowsArgumentNull()
    {
        var builder = new SecurityTestContextBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithClaim(null!, "v"));
        Assert.Throws<ArgumentNullException>(() => builder.WithClaim("t", null!));
    }

    [Fact]
    public void BuildUserContext_Default_AuthenticatedUserWithDefaultSubject()
    {
        var context = new SecurityTestContextBuilder().BuildUserContext();

        Assert.Equal(IdentityKind.User, context.IdentityKind);
        Assert.True(context.IsAuthenticated);
        Assert.Equal(FakeUserContext.DefaultSubjectId, context.SubjectId);
        Assert.Null(context.ClientId);
        Assert.Null(context.TenantId);
        Assert.Empty(context.Roles);
        Assert.Empty(context.Permissions);
        Assert.Empty(context.AuthenticationMethods);
        Assert.Null(context.AuthTime);
    }

    [Fact]
    public void BuildUserContext_AllFields_ProjectedExactly()
    {
        var context = new SecurityTestContextBuilder()
            .WithSubjectId("user-42")
            .WithClientId("spa-client")
            .WithTenantId(TenantId)
            .WithSessionId("session-7")
            .WithName("Ada")
            .WithEmail("ada@example.test")
            .WithRoles("Admin", "Editor")
            .WithPermissions("orders:write")
            .WithAuthenticationMethods("pwd", "otp")
            .WithAuthContextClassReference("urn:acr:silver")
            .WithAuthTime(AuthTime)
            .WithClaim("groups", "finance")
            .BuildUserContext();

        Assert.Equal("user-42", context.SubjectId);
        Assert.Equal("spa-client", context.ClientId);
        Assert.Equal(TenantId, context.TenantId);
        Assert.Equal("session-7", context.SessionId);
        Assert.Equal("Ada", context.Name);
        Assert.Equal("ada@example.test", context.Email);
        Assert.Equal(["Admin", "Editor"], context.Roles);
        Assert.Equal(["orders:write"], context.Permissions);
        Assert.Equal(["pwd", "otp"], context.AuthenticationMethods);
        Assert.Equal("urn:acr:silver", context.AuthContextClassReference);
        Assert.Equal(AuthTime, context.AuthTime);
        Assert.Equal("finance", context.FindClaim("groups"));
    }

    [Theory]
    [InlineData(IdentityKind.Anonymous, false)]
    [InlineData(IdentityKind.System, true)]
    public void BuildUserContext_KindWithoutSubject_SubjectIdNull(IdentityKind kind, bool authenticated)
    {
        var context = new SecurityTestContextBuilder().WithSubjectId("user-42").WithIdentityKind(kind).BuildUserContext();

        Assert.Equal(kind, context.IdentityKind);
        Assert.Equal(authenticated, context.IsAuthenticated);
        Assert.Null(context.SubjectId);
    }

    [Fact]
    public void BuildUserContext_ServicePrincipal_KeepsSubject()
    {
        var context = new SecurityTestContextBuilder()
            .WithSubjectId("svc-1")
            .WithIdentityKind(IdentityKind.ServicePrincipal)
            .BuildUserContext();

        Assert.Equal(IdentityKind.ServicePrincipal, context.IdentityKind);
        Assert.Equal("svc-1", context.SubjectId);
    }

    [Fact]
    public void BuildUserContext_BuilderChangedAfterBuild_EarlierContextUnchanged()
    {
        var builder = new SecurityTestContextBuilder().WithRoles("Admin").WithClaim("groups", "a");
        var context = builder.BuildUserContext();

        builder.WithRoles("Viewer").WithClaim("groups", "b");

        Assert.Equal(["Admin"], context.Roles);
        Assert.Equal(["a"], context.FindClaims("groups"));
    }

    [Fact]
    public void BuildUserContext_SameInputs_ChecksAgreeWithPrincipalClaims()
    {
        var builder = new SecurityTestContextBuilder()
            .WithRoles("Admin")
            .WithPermissions("orders:write")
            .WithAuthenticationMethods("otp")
            .WithAuthTime(AuthTime);

        var context = builder.BuildUserContext();
        var principal = builder.Build();

        Assert.True(context.HasRole("Admin"));
        Assert.True(principal.IsInRole("Admin"));
        Assert.True(context.HasPermission("orders:write"));
        Assert.Contains("orders:write", Single(principal, "scope").Split(' '));
        Assert.True(context.WasAuthenticatedWith("otp"));
        Assert.True(principal.HasClaim("amr", "otp"));
        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), AuthTime.AddMinutes(5)));
    }

    // Mirrors SharedKernel.Security.Oidc's default claim mapping (sub, azp, tenant_id, sid, name, email, roles,
    // space-delimited scope, amr, amr_time, acr, auth_time, idtyp=app), which this project cannot reference directly.
    [Theory]
    [InlineData(IdentityKind.User)]
    [InlineData(IdentityKind.ServicePrincipal)]
    public void Build_MappedLikeOidcDefaults_MatchesBuildUserContext(IdentityKind kind)
    {
        var builder = new SecurityTestContextBuilder()
            .WithIdentityKind(kind)
            .WithSubjectId("subject-42")
            .WithClientId("client-9")
            .WithTenantId(TenantId)
            .WithSessionId("session-7")
            .WithName("Ada")
            .WithEmail("ada@example.test")
            .WithRoles("Admin", "Editor")
            .WithPermissions("orders:read", "orders:write")
            .WithAuthenticationMethods("pwd", "otp")
            .WithAuthenticationMethodTime("otp", AuthTime.AddMinutes(30))
            .WithAuthContextClassReference("urn:acr:silver")
            .WithAuthTime(AuthTime);

        var expected = builder.BuildUserContext();
        var identity = (ClaimsIdentity)builder.Build().Identity!;

        Assert.True(identity.IsAuthenticated);
        Assert.Equal("Bearer", identity.AuthenticationType);
        Assert.Equal(expected.IdentityKind == IdentityKind.ServicePrincipal, identity.HasClaim("idtyp", "app"));
        Assert.Equal(expected.SubjectId, identity.FindFirst(SecurityClaimTypes.Subject)?.Value);
        Assert.Equal(expected.ClientId, identity.FindFirst(SecurityClaimTypes.AuthorizedParty)?.Value);
        Assert.Equal(expected.TenantId, Guid.Parse(identity.FindFirst(SecurityClaimTypes.TenantId)!.Value));
        Assert.Equal(expected.SessionId, identity.FindFirst(SecurityClaimTypes.SessionId)?.Value);
        Assert.Equal(expected.Name, identity.FindFirst(SecurityClaimTypes.Name)?.Value);
        Assert.Equal(expected.Email, identity.FindFirst(SecurityClaimTypes.Email)?.Value);
        Assert.Equal(expected.Roles, identity.FindAll(SecurityClaimTypes.Roles).Select(claim => claim.Value));
        Assert.Equal(
            expected.Permissions,
            identity.FindAll(SecurityClaimTypes.Scope).SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Equal(expected.AuthenticationMethods, identity.FindAll(SecurityClaimTypes.AuthenticationMethod).Select(claim => claim.Value));
        Assert.Equal(expected.AuthenticationMethodTimes, AuthenticationMethodTimeClaim.Read(identity.Claims));
        Assert.Equal(expected.AuthContextClassReference, identity.FindFirst(SecurityClaimTypes.AuthContextClassReference)?.Value);
        Assert.Equal(
            expected.AuthTime,
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(identity.FindFirst(SecurityClaimTypes.AuthTime)!.Value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void BuildUserContext_FindClaimStandardClaim_MatchesBuildPrincipal()
    {
        var builder = new SecurityTestContextBuilder().WithTenantId(TenantId).WithRoles("Admin");

        var context = builder.BuildUserContext();
        var principal = builder.Build();

        Assert.Equal(principal.FindFirst("sub")?.Value, context.FindClaim("sub"));
        Assert.Equal(principal.FindFirst("tenant_id")?.Value, context.FindClaim("tenant_id"));
        Assert.Equal(principal.FindAll("roles").Select(claim => claim.Value), context.FindClaims("roles"));
    }

    private static string Single(ClaimsPrincipal principal, string claimType) =>
        Assert.Single(principal.FindAll(claimType)).Value;
}
