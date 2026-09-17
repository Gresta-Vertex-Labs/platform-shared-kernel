using System.Security.Claims;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Authentication;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Authentication;

public sealed class OidcUserContextMapperTests
{
    private readonly InMemoryLogger<OidcUserContextMapper> _logger = new();

    [Fact]
    public void AuthenticationType_IsBearerScheme()
    {
        Assert.Equal("Bearer", CreateMapper().AuthenticationType);
    }

    [Fact]
    public void Map_NullIdentity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreateMapper().Map(null!));
    }

    [Fact]
    public void Map_SubjectOnly_IsUser()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1")));

        Assert.Equal(IdentityKind.User, user.IdentityKind);
        Assert.True(user.IsAuthenticated);
        Assert.Equal("user-1", user.SubjectId);
        Assert.Null(user.ClientId);
    }

    [Fact]
    public void Map_SubjectAndDifferentAuthorizedParty_IsUserWithClientId()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("azp", "web-app")));

        Assert.Equal(IdentityKind.User, user.IdentityKind);
        Assert.Equal("user-1", user.SubjectId);
        Assert.Equal("web-app", user.ClientId);
    }

    [Theory]
    [InlineData("idtyp", "app")]
    [InlineData("gty", "client-credentials")]
    public void Map_DefaultApplicationTokenClaim_IsServicePrincipal(string type, string value)
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "object-id"), ("azp", "daemon"), (type, value)));

        Assert.Equal(IdentityKind.ServicePrincipal, user.IdentityKind);
        Assert.Equal("object-id", user.SubjectId);
        Assert.Equal("daemon", user.ClientId);
    }

    [Theory]
    [InlineData("idtyp", "user")]
    [InlineData("gty", "password")]
    public void Map_ApplicationClaimTypeWithOtherValue_IsUser(string type, string value)
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), (type, value)));

        Assert.Equal(IdentityKind.User, user.IdentityKind);
    }

    [Fact]
    public void Map_SubjectEqualsClientId_IsServicePrincipal()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "service-a"), ("client_id", "service-a")));

        Assert.Equal(IdentityKind.ServicePrincipal, user.IdentityKind);
        Assert.Equal("service-a", user.SubjectId);
    }

    [Fact]
    public void Map_SubjectDiffersFromClientIdOnlyByCase_IsUser()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "Service-A"), ("client_id", "service-a")));

        Assert.Equal(IdentityKind.User, user.IdentityKind);
    }

    [Theory]
    [InlineData("azp")]
    [InlineData("client_id")]
    [InlineData("appid")]
    public void Map_ClientIdWithoutSubject_IsServicePrincipalWithClientIdAsSubject(string clientIdClaim)
    {
        IUserContext user = CreateMapper().Map(Identity((clientIdClaim, "daemon")));

        Assert.Equal(IdentityKind.ServicePrincipal, user.IdentityKind);
        Assert.Equal("daemon", user.SubjectId);
        Assert.Equal("daemon", user.ClientId);
    }

    [Fact]
    public void Map_SeveralClientIdClaims_FirstInDefaultOrderWins()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("appid", "third"), ("client_id", "second"), ("azp", "first")));

        Assert.Equal("first", user.ClientId);
    }

    [Fact]
    public void Map_CustomApplicationTokenClaims_ReplaceDefaults()
    {
        var options = new OidcAuthenticationOptions();
        options.Claims.ApplicationTokenClaims["token_use"] = "machine";
        OidcUserContextMapper mapper = CreateMapper(options);

        IUserContext custom = mapper.Map(Identity(("sub", "svc"), ("azp", "daemon"), ("token_use", "machine")));
        IUserContext formerDefault = mapper.Map(Identity(("sub", "user-1"), ("azp", "web"), ("idtyp", "app")));

        Assert.Equal(IdentityKind.ServicePrincipal, custom.IdentityKind);
        Assert.Equal(IdentityKind.User, formerDefault.IdentityKind);
    }

    [Fact]
    public void Map_NeitherSubjectNorClientId_IsAnonymousAndLogs()
    {
        IUserContext user = CreateMapper().Map(Identity(("email", "someone@example.test")));

        Assert.Same(AnonymousUserContext.Instance, user);
        _logger.Records.ShouldHaveLogged(new(12100));
    }

    [Fact]
    public void Map_EmptySubject_IsTreatedAsMissing()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", string.Empty)));

        Assert.Same(AnonymousUserContext.Instance, user);
    }

    [Fact]
    public void Map_ApplicationClaimWithoutSubjectOrClientId_IsAnonymous()
    {
        IUserContext user = CreateMapper().Map(Identity(("idtyp", "app")));

        Assert.Same(AnonymousUserContext.Instance, user);
        _logger.Records.ShouldHaveLogged(new(12100));
    }

    [Fact]
    public void Map_ValidTenantGuid_SetsTenantId()
    {
        Guid tenant = Guid.NewGuid();

        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("tenant_id", tenant.ToString())));

        Assert.Equal(tenant, user.TenantId);
        _logger.Records.ShouldNotHaveLogged(new(12101));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Map_InvalidOrEmptyTenant_HasNoTenantAndLogs(string value)
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("tenant_id", value)));

        Assert.Null(user.TenantId);
        _logger.Records.ShouldHaveLoggedWithProperty(new(12101), "TenantClaimType", "tenant_id");
    }

    [Fact]
    public void Map_NoTenantClaim_HasNoTenantAndDoesNotLog()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1")));

        Assert.Null(user.TenantId);
        _logger.Records.ShouldNotHaveLogged(new(12101));
    }

    [Fact]
    public void Map_SessionIdPresent_PrefersSidOverTokenIds()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("uti", "uti-1"), ("jti", "jti-1"), ("sid", "sid-1")));

        Assert.Equal("sid-1", user.SessionId);
    }

    [Fact]
    public void Map_NoSid_FallsBackToJti()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("uti", "uti-1"), ("jti", "jti-1")));

        Assert.Equal("jti-1", user.SessionId);
    }

    [Fact]
    public void Map_OnlyUti_UsesUti()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("uti", "uti-1")));

        Assert.Equal("uti-1", user.SessionId);
    }

    [Fact]
    public void Map_NoSessionClaims_HasNoSessionId()
    {
        Assert.Null(CreateMapper().Map(Identity(("sub", "user-1"))).SessionId);
    }

    [Fact]
    public void Map_EntraClaimSettings_ReadsOidAndTid()
    {
        Guid tenant = Guid.NewGuid();
        var options = new OidcAuthenticationOptions();
        options.Claims.SubjectClaimType = "oid";
        options.Claims.TenantClaimType = "tid";

        IUserContext user = CreateMapper(options).Map(Identity(
            ("sub", "pairwise-subject"),
            ("oid", "object-id"),
            ("tid", tenant.ToString()),
            ("tenant_id", Guid.NewGuid().ToString())));

        Assert.Equal("object-id", user.SubjectId);
        Assert.Equal(tenant, user.TenantId);
    }

    [Fact]
    public void Map_CustomListClaimTypes_ReplaceDefaults()
    {
        var options = new OidcAuthenticationOptions();
        options.Claims.PermissionClaimTypes.Add("permissions");
        options.Claims.ClientIdClaimTypes.Add("cid");
        options.Claims.SessionIdClaimTypes.Add("session");

        IUserContext user = CreateMapper(options).Map(Identity(
            ("sub", "user-1"),
            ("scope", "ignored.scope"),
            ("permissions", "orders.read"),
            ("azp", "ignored-client"),
            ("cid", "custom-client"),
            ("sid", "ignored-sid"),
            ("session", "custom-session")));

        Assert.Equal(["orders.read"], user.Permissions);
        Assert.Equal("custom-client", user.ClientId);
        Assert.Equal("custom-session", user.SessionId);
    }

    [Fact]
    public void Map_CustomSingleClaimTypes_AreRead()
    {
        var options = new OidcAuthenticationOptions();
        options.Claims.NameClaimType = "preferred_username";
        options.Claims.EmailClaimType = "upn";
        options.Claims.RoleClaimType = "groups";
        options.Claims.AuthenticationMethodClaimType = "methods";
        options.Claims.AuthContextClassReferenceClaimType = "loa";
        options.Claims.AuthTimeClaimType = "login_time";

        IUserContext user = CreateMapper(options).Map(Identity(
            ("sub", "user-1"),
            ("preferred_username", "ada"),
            ("upn", "ada@corp.test"),
            ("groups", "finance"),
            ("roles", "ignored"),
            ("methods", "otp"),
            ("loa", "high"),
            ("login_time", "1700000000")));

        Assert.Equal("ada", user.Name);
        Assert.Equal("ada@corp.test", user.Email);
        Assert.Equal(["finance"], user.Roles);
        Assert.Equal(["otp"], user.AuthenticationMethods);
        Assert.Equal("high", user.AuthContextClassReference);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), user.AuthTime);
    }

    [Fact]
    public void Map_RolesAndMethods_AreDistinctAndSkipEmptyValues()
    {
        IUserContext user = CreateMapper().Map(Identity(
            ("sub", "user-1"),
            ("roles", "admin"),
            ("roles", string.Empty),
            ("roles", "admin"),
            ("roles", "Admin"),
            ("amr", "pwd"),
            ("amr", "pwd")));

        Assert.Equal(["admin", "Admin"], user.Roles);
        Assert.Equal(["pwd"], user.AuthenticationMethods);
    }

    [Fact]
    public void Map_PermissionsFromScopeAndScp_AreSplitAndDistinct()
    {
        IUserContext user = CreateMapper().Map(Identity(
            ("sub", "user-1"),
            ("scope", "  orders.read   orders.write "),
            ("scp", "orders.read profile")));

        Assert.Equal(["orders.read", "orders.write", "profile"], user.Permissions);
        Assert.True(user.HasPermission("profile"));
        Assert.False(user.HasPermission("orders"));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("soon")]
    [InlineData("99999999999999999")]
    public void Map_InvalidAuthTime_IsNull(string value)
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("auth_time", value)));

        Assert.Null(user.AuthTime);
    }

    [Fact]
    public void Map_ClaimsAreExposedThroughFindClaim()
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("department", "risk"), ("department", "audit")));

        Assert.Equal("risk", user.FindClaim("department"));
        Assert.Equal(["risk", "audit"], user.FindClaims("department"));
    }

    [Theory]
    [InlineData("{\"jkt\":\"thumb\"}", true)]
    [InlineData("{\"x5t#S256\":\"thumb\"}", true)]
    [InlineData("{}", false)]
    public void Map_Confirmation_SetsIsSenderConstrained(string confirmation, bool expected)
    {
        IUserContext user = CreateMapper().Map(Identity(("sub", "user-1"), ("cnf", confirmation)));

        Assert.Equal(expected, user.IsSenderConstrained);
    }

    [Fact]
    public void Map_NoConfirmation_IsNotSenderConstrained()
    {
        Assert.False(CreateMapper().Map(Identity(("sub", "user-1"))).IsSenderConstrained);
    }

    private OidcUserContextMapper CreateMapper(OidcAuthenticationOptions? options = null) =>
        new(new StaticOptionsMonitor<OidcAuthenticationOptions>(options ?? new OidcAuthenticationOptions()), _logger);

    private static ClaimsIdentity Identity(params (string Type, string Value)[] claims) =>
        new(claims.Select(claim => new Claim(claim.Type, claim.Value)), OidcAuthenticationDefaults.AuthenticationScheme);
}
