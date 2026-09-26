using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
using System.Security.Claims;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class UserContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ActorKind.Anonymous)]
    [InlineData(ActorKind.System)]
    [InlineData((ActorKind)99)]
    public void Constructor_KindWithoutSubject_ThrowsArgumentOutOfRange(ActorKind kind)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new UserContext(kind, "subject-1"));

        Assert.Equal("actorKind", exception.ParamName);
    }

    [Theory]
    [InlineData(ActorKind.User)]
    [InlineData(ActorKind.Service)]
    public void Constructor_KindWithSubject_SetsKindSubjectAndAuthenticated(ActorKind kind)
    {
        var context = new UserContext(kind, "subject-1");

        Assert.Equal(kind, context.ActorKind);
        Assert.Equal("subject-1", context.SubjectId);
        Assert.True(context.IsAuthenticated);
    }

    [Fact]
    public void Constructor_NullSubject_Throws()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => new UserContext(ActorKind.User, null!));

        Assert.Equal("subjectId", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void Constructor_EmptyOrWhitespaceSubject_ThrowsArgumentException(string subjectId)
    {
        var exception = Assert.Throws<ArgumentException>(() => new UserContext(ActorKind.User, subjectId));

        Assert.Equal("subjectId", exception.ParamName);
    }

    [Fact]
    public void Constructor_InvalidKindAndEmptySubject_ReportsKindFirst()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserContext(ActorKind.Anonymous, ""));
    }

    [Fact]
    public void Constructor_OnlyRequiredArguments_OptionalMembersHaveEmptyDefaults()
    {
        var context = new UserContext(ActorKind.User, "subject-1");

        Assert.Null(context.ClientId);
        Assert.Null(context.TenantId);
        Assert.Null(context.SessionId);
        Assert.Null(context.Name);
        Assert.Null(context.Email);
        Assert.Empty(context.Roles);
        Assert.Empty(context.Permissions);
        Assert.Empty(context.AuthenticationMethods);
        Assert.Null(context.AuthContextClassReference);
        Assert.Null(context.AuthTime);
        Assert.False(context.IsSenderConstrained);
        Assert.Null(context.FindClaim(SecurityClaimTypes.Subject));
        Assert.Empty(context.FindClaims(SecurityClaimTypes.Subject));
    }

    [Fact]
    public void InitProperties_AreReturnedAsSet()
    {
        var tenantId = new TenantId(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));
        var authTime = Now.AddMinutes(-3);

        var context = new UserContext(ActorKind.Service, "subject-1")
        {
            ClientId = "client-1",
            TenantId = tenantId,
            SessionId = "session-1",
            Name = "Ada",
            Email = "ada@example.test",
            Roles = ["admin"],
            Permissions = ["orders:read", "orders:write"],
            AuthenticationMethods = ["pwd", "otp"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset> { ["otp"] = authTime.AddMinutes(1) },
            AuthContextClassReference = "urn:acr:silver",
            AuthTime = authTime,
            IsSenderConstrained = true,
        };

        Assert.Equal("client-1", context.ClientId);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("session-1", context.SessionId);
        Assert.Equal("Ada", context.Name);
        Assert.Equal("ada@example.test", context.Email);
        Assert.Equal(["admin"], context.Roles);
        Assert.Equal(["orders:read", "orders:write"], context.Permissions);
        Assert.Equal(["pwd", "otp"], context.AuthenticationMethods);
        Assert.Equal(authTime.AddMinutes(1), Assert.Single(context.AuthenticationMethodTimes, pair => pair.Key == "otp").Value);
        Assert.Equal("urn:acr:silver", context.AuthContextClassReference);
        Assert.Equal(authTime, context.AuthTime);
        Assert.True(context.IsSenderConstrained);
    }

    [Fact]
    public void FindClaim_SeveralValues_ReturnsFirstInOrder()
    {
        var context = WithClaims(("roles", "first"), ("other", "x"), ("roles", "second"));

        Assert.Equal("first", context.FindClaim("roles"));
    }

    [Fact]
    public void FindClaim_Missing_ReturnsNull()
    {
        var context = WithClaims(("roles", "first"));

        Assert.Null(context.FindClaim("scope"));
    }

    [Fact]
    public void FindClaim_TypeDiffersInCase_ReturnsNull()
    {
        var context = WithClaims(("tenant_id", "value"));

        Assert.Null(context.FindClaim("Tenant_Id"));
        Assert.Null(context.FindClaim("TENANT_ID"));
    }

    [Fact]
    public void FindClaim_EmptyValue_ReturnsEmptyString()
    {
        var context = WithClaims(("acr", string.Empty));

        Assert.Equal(string.Empty, context.FindClaim("acr"));
    }

    [Fact]
    public void FindClaims_SeveralValues_ReturnsAllInCredentialOrder()
    {
        var context = WithClaims(("amr", "pwd"), ("sub", "s"), ("amr", "otp"), ("amr", "pwd"));

        Assert.Equal(["pwd", "otp", "pwd"], context.FindClaims("amr"));
    }

    [Fact]
    public void FindClaims_Missing_ReturnsEmpty()
    {
        var context = WithClaims(("amr", "pwd"));

        Assert.Empty(context.FindClaims("roles"));
    }

    [Fact]
    public void FindClaims_TypeDiffersInCase_ReturnsEmpty()
    {
        var context = WithClaims(("amr", "pwd"));

        Assert.Empty(context.FindClaims("AMR"));
    }

    [Fact]
    public void FindClaimAndFindClaims_NullType_ThrowArgumentNull()
    {
        var context = WithClaims(("amr", "pwd"));

        Assert.Throws<ArgumentNullException>(() => context.FindClaim(null!));
        Assert.Throws<ArgumentNullException>(() => context.FindClaims(null!));
    }

    [Fact]
    public void Constructor_ClaimsSourceMutatedAfterwards_ContextKeepsOriginalClaims()
    {
        var source = new List<Claim> { new("roles", "admin") };
        var context = new UserContext(ActorKind.User, "subject-1", source);

        source.Add(new Claim("roles", "injected"));
        source[0] = new Claim("roles", "replaced");

        Assert.Equal(["admin"], context.FindClaims("roles"));
    }

    [Fact]
    public void HasRole_ExactMatch_ReturnsTrue()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { Roles = ["reader", "Admin"] };

        Assert.True(context.HasRole("Admin"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin ")]
    [InlineData("Adm")]
    public void HasRole_CaseOrTextDiffers_ReturnsFalse(string role)
    {
        var context = new UserContext(ActorKind.User, "subject-1") { Roles = ["Admin"] };

        Assert.False(context.HasRole(role));
    }

    [Fact]
    public void HasPermission_ExactMatch_ReturnsTrue()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { Permissions = ["orders:read", "orders:write"] };

        Assert.True(context.HasPermission("orders:write"));
    }

    [Theory]
    [InlineData("Orders:Write")]
    [InlineData("ORDERS:WRITE")]
    [InlineData("orders")]
    public void HasPermission_CaseOrTextDiffers_ReturnsFalse(string permission)
    {
        var context = new UserContext(ActorKind.User, "subject-1") { Permissions = ["orders:write"] };

        Assert.False(context.HasPermission(permission));
    }

    [Fact]
    public void WasAuthenticatedWith_ExactMatch_ReturnsTrue()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthenticationMethods = ["pwd", "otp"] };

        Assert.True(context.WasAuthenticatedWith("otp"));
    }

    [Theory]
    [InlineData("OTP")]
    [InlineData("Otp")]
    [InlineData("mfa")]
    public void WasAuthenticatedWith_CaseOrMethodDiffers_ReturnsFalse(string method)
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthenticationMethods = ["pwd", "otp"] };

        Assert.False(context.WasAuthenticatedWith(method));
    }

    [Fact]
    public void RoleChecks_NoValuesGranted_ReturnFalse()
    {
        var context = new UserContext(ActorKind.User, "subject-1");

        Assert.False(context.HasRole("admin"));
        Assert.False(context.HasPermission("orders:write"));
        Assert.False(context.WasAuthenticatedWith("otp"));
    }

    [Fact]
    public void RoleChecks_NullArgument_ThrowArgumentNull()
    {
        var context = new UserContext(ActorKind.User, "subject-1");

        Assert.Throws<ArgumentNullException>(() => context.HasRole(null!));
        Assert.Throws<ArgumentNullException>(() => context.HasPermission(null!));
        Assert.Throws<ArgumentNullException>(() => context.WasAuthenticatedWith(null!));
    }

    [Fact]
    public void RoleChecks_ValueGrantedOnlyAsClaim_ReturnFalse()
    {
        // Checks read the mapped collections, never raw claims, so an unmapped claim grants nothing.
        var context = new UserContext(
            ActorKind.User,
            "subject-1",
            [new Claim("roles", "admin"), new Claim("scope", "orders:write"), new Claim("amr", "otp")]);

        Assert.False(context.HasRole("admin"));
        Assert.False(context.HasPermission("orders:write"));
        Assert.False(context.WasAuthenticatedWith("otp"));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AgeExactlyMaxAge_ReturnsTrue()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthTime = Now.AddMinutes(-5) };

        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AgeOneTickOverMaxAge_ReturnsFalse()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthTime = Now.AddMinutes(-5).AddTicks(-1) };

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AgeUnderMaxAge_ReturnsTrue()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthTime = Now.AddMinutes(-4) };

        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_ZeroMaxAgeAndAuthenticatedNow_ReturnsTrue()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthTime = Now };

        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.Zero, Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_UnknownAuthTime_ReturnsFalse()
    {
        var context = new UserContext(ActorKind.User, "subject-1");

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.MaxValue, Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_SameInstantDifferentOffset_ComparesInstants()
    {
        var authTime = new DateTimeOffset(2026, 9, 16, 14, 56, 0, TimeSpan.FromHours(3));
        var context = new UserContext(ActorKind.User, "subject-1") { AuthTime = authTime };

        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(4), Now));
        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(3), Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AuthTimeFarInFuture_ReturnsFalse()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthTime = Now.AddDays(1) };

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void Roles_SourceListMutatedAfterConstruction_ContextUnchanged()
    {
        var roles = new List<string> { "reader" };
        var context = new UserContext(ActorKind.User, "subject-1") { Roles = roles };

        roles.Add("admin");

        Assert.False(context.HasRole("admin"));
    }

    [Fact]
    public void AuthenticationMethodTimes_NotSet_IsEmpty()
    {
        Assert.Empty(new UserContext(ActorKind.User, "subject-1").AuthenticationMethodTimes);
    }

    [Fact]
    public void GetAuthenticationMethodTime_RecordedMethod_ReturnsItsTime()
    {
        var context = new UserContext(ActorKind.User, "subject-1")
        {
            AuthenticationMethods = ["pwd", "otp"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset> { ["otp"] = Now.AddMinutes(-2) },
            AuthTime = Now.AddHours(-3),
        };

        Assert.Equal(Now.AddMinutes(-2), context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_MethodWithoutRecordedTime_DatesFromTheSignIn()
    {
        // A method the credential carried was verified when the user signed in.
        var context = new UserContext(ActorKind.User, "subject-1")
        {
            AuthenticationMethods = ["pwd", "otp"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset> { ["otp"] = Now.AddMinutes(-2) },
            AuthTime = Now.AddHours(-3),
        };

        Assert.Equal(Now.AddHours(-3), context.GetAuthenticationMethodTime("pwd"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_MethodWithoutAnyTime_ReturnsNull()
    {
        var context = new UserContext(ActorKind.User, "subject-1") { AuthenticationMethods = ["otp"] };

        Assert.Null(context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_TimeForAMethodTheCallerDoesNotHave_ReturnsNull()
    {
        // AuthenticationMethods decides which methods the caller has; a time alone proves nothing.
        var context = new UserContext(ActorKind.User, "subject-1")
        {
            AuthenticationMethods = ["pwd"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset> { ["otp"] = Now },
            AuthTime = Now,
        };

        Assert.Null(context.GetAuthenticationMethodTime("otp"));
        Assert.Null(context.GetAuthenticationMethodTime("hwk"));
    }

    [Theory]
    [InlineData("OTP")]
    [InlineData("Otp")]
    public void GetAuthenticationMethodTime_CaseDiffers_ReturnsNull(string method)
    {
        var context = new UserContext(ActorKind.User, "subject-1")
        {
            AuthenticationMethods = ["otp"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset> { ["otp"] = Now },
        };

        Assert.Null(context.GetAuthenticationMethodTime(method));
    }

    [Fact]
    public void GetAuthenticationMethodTime_TimeOnlyAsRawClaim_IsNotRead()
    {
        // Like the other checks, it reads what the mapper mapped, never raw claims.
        var context = new UserContext(
            ActorKind.User,
            "subject-1",
            [AuthenticationMethodTimeClaim.Create("otp", Now)])
        {
            AuthenticationMethods = ["otp"],
        };

        Assert.Null(context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_NullMethod_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UserContext(ActorKind.User, "subject-1").GetAuthenticationMethodTime(null!));
    }

    [Fact]
    public void AuthenticationMethodTimes_IsACopyKeyedOrdinally_AndReadOnly()
    {
        var source = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase) { ["otp"] = Now };
        var context = new UserContext(ActorKind.User, "subject-1") { AuthenticationMethodTimes = source };

        source["hwk"] = Now;

        Assert.Equal(["otp"], context.AuthenticationMethodTimes.Keys);
        Assert.False(context.AuthenticationMethodTimes.ContainsKey("OTP"));
        Assert.True(((ICollection<KeyValuePair<string, DateTimeOffset>>)context.AuthenticationMethodTimes).IsReadOnly);
    }

    [Fact]
    public void AuthenticationMethodTimes_SetToNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new UserContext(ActorKind.User, "subject-1") { AuthenticationMethodTimes = null! });
    }

    [Fact]
    public void GetAuthenticationMethodTime_ImplementationWithoutIt_DefaultsToNull()
    {
        // Implementations written before the member keep compiling and report no time, so a maximum age refuses them.
        IUserContext context = new ContextWithoutMethodTimes();

        Assert.True(context.WasAuthenticatedWith("otp"));
        Assert.Null(context.GetAuthenticationMethodTime("otp"));
    }

    private static UserContext WithClaims(params (string Type, string Value)[] claims) =>
        new(ActorKind.User, "subject-1", claims.Select(claim => new Claim(claim.Type, claim.Value)));

    // An IUserContext written before GetAuthenticationMethodTime existed: it does not implement the member.
    private sealed class ContextWithoutMethodTimes : IUserContext
    {
        public ActorKind ActorKind => ActorKind.User;

        public bool IsAuthenticated => true;

        public string? SubjectId => "subject-1";

        public string? ClientId => null;

        public TenantId? TenantId => null;

        public string? SessionId => null;

        public string? Name => null;

        public string? Email => null;

        public IReadOnlyCollection<string> Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public IReadOnlyCollection<string> AuthenticationMethods => ["otp"];

        public string? AuthContextClassReference => null;

        public DateTimeOffset? AuthTime => Now;

        public bool IsSenderConstrained => false;

        public string? FindClaim(string claimType) => null;

        public IReadOnlyList<string> FindClaims(string claimType) => [];

        public bool HasRole(string role) => false;

        public bool HasPermission(string permission) => false;

        public bool WasAuthenticatedWith(string method) => AuthenticationMethods.Contains(method, StringComparer.Ordinal);

        public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
    }
}
