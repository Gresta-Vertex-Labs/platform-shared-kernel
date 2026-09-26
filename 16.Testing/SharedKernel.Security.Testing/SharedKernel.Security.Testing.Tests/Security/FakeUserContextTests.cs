using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

public sealed class FakeUserContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Defaults_AuthenticatedUserWithDefaultSubject()
    {
        var context = new FakeUserContext();

        Assert.Equal(ActorKind.User, context.ActorKind);
        Assert.True(context.IsAuthenticated);
        Assert.Equal(FakeUserContext.DefaultSubjectId, context.SubjectId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", FakeUserContext.DefaultSubjectId);
    }

    [Fact]
    public void Constructor_Defaults_OptionalMembersNullOrEmpty()
    {
        var context = new FakeUserContext();

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
        Assert.Empty(context.Claims);
    }

    [Theory]
    [InlineData(ActorKind.Anonymous, false)]
    [InlineData(ActorKind.User, true)]
    [InlineData(ActorKind.Service, true)]
    [InlineData(ActorKind.System, true)]
    public void IsAuthenticated_FollowsActorKind(ActorKind kind, bool expected)
    {
        var context = new FakeUserContext { ActorKind = kind };

        Assert.Equal(expected, context.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_MatchesRealAnonymousAndSystemContexts()
    {
        Assert.Equal(AnonymousUserContext.Instance.IsAuthenticated, new FakeUserContext { ActorKind = ActorKind.Anonymous }.IsAuthenticated);
        Assert.Equal(SystemUserContext.Instance.IsAuthenticated, new FakeUserContext { ActorKind = ActorKind.System }.IsAuthenticated);
    }

    [Fact]
    public void Properties_WhenSet_ReturnSetValues()
    {
        TenantId tenantId = new TenantId(Guid.Parse("6a3b1f0e-9c2d-4e7a-8b5f-1d2c3e4f5a6b"));
        var context = new FakeUserContext
        {
            ActorKind = ActorKind.Service,
            SubjectId = "svc-orders",
            ClientId = "orders-client",
            TenantId = tenantId,
            SessionId = "session-1",
            Name = "Orders",
            Email = "orders@example.test",
            Roles = ["admin"],
            Permissions = ["orders:write"],
            AuthenticationMethods = ["pwd"],
            AuthContextClassReference = "urn:acr:gold",
            AuthTime = Now,
            IsSenderConstrained = true,
        };

        Assert.Equal(ActorKind.Service, context.ActorKind);
        Assert.Equal("svc-orders", context.SubjectId);
        Assert.Equal("orders-client", context.ClientId);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("session-1", context.SessionId);
        Assert.Equal("Orders", context.Name);
        Assert.Equal("orders@example.test", context.Email);
        Assert.Equal(["admin"], context.Roles);
        Assert.Equal(["orders:write"], context.Permissions);
        Assert.Equal(["pwd"], context.AuthenticationMethods);
        Assert.Equal("urn:acr:gold", context.AuthContextClassReference);
        Assert.Equal(Now, context.AuthTime);
        Assert.True(context.IsSenderConstrained);
    }

    [Fact]
    public void FindClaim_SeveralValues_ReturnsFirst()
    {
        var context = new FakeUserContext { Claims = [new("groups", "a"), new("other", "x"), new("groups", "b")] };

        Assert.Equal("a", context.FindClaim("groups"));
    }

    [Fact]
    public void FindClaim_MissingOrCaseDiffers_ReturnsNull()
    {
        var context = new FakeUserContext { Claims = [new("groups", "a")] };

        Assert.Null(context.FindClaim("missing"));
        Assert.Null(context.FindClaim("Groups"));
    }

    [Fact]
    public void FindClaims_SeveralValues_ReturnsAllInOrder()
    {
        var context = new FakeUserContext { Claims = [new("groups", "b"), new("other", "x"), new("groups", "a")] };

        Assert.Equal(["b", "a"], context.FindClaims("groups"));
    }

    [Fact]
    public void FindClaims_MissingOrCaseDiffers_ReturnsEmpty()
    {
        var context = new FakeUserContext { Claims = [new("groups", "a")] };

        Assert.Empty(context.FindClaims("missing"));
        Assert.Empty(context.FindClaims("GROUPS"));
    }

    [Fact]
    public void FindClaimAndFindClaims_NullType_ThrowArgumentNull()
    {
        var context = new FakeUserContext();

        Assert.Throws<ArgumentNullException>(() => context.FindClaim(null!));
        Assert.Throws<ArgumentNullException>(() => context.FindClaims(null!));
    }

    [Fact]
    public void HasRole_ExactMatch_ReturnsTrue()
    {
        var context = new FakeUserContext { Roles = ["reader", "Admin"] };

        Assert.True(context.HasRole("Admin"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Viewer")]
    public void HasRole_CaseOrNameDiffers_ReturnsFalse(string role)
    {
        var context = new FakeUserContext { Roles = ["Admin"] };

        Assert.False(context.HasRole(role));
    }

    [Fact]
    public void HasPermission_ExactMatch_ReturnsTrue()
    {
        var context = new FakeUserContext { Permissions = ["orders:read", "orders:write"] };

        Assert.True(context.HasPermission("orders:write"));
    }

    [Theory]
    [InlineData("ORDERS:WRITE")]
    [InlineData("Orders:Write")]
    [InlineData("orders:delete")]
    public void HasPermission_CaseOrNameDiffers_ReturnsFalse(string permission)
    {
        var context = new FakeUserContext { Permissions = ["orders:write"] };

        Assert.False(context.HasPermission(permission));
    }

    [Fact]
    public void WasAuthenticatedWith_ExactMatch_ReturnsTrue()
    {
        var context = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"] };

        Assert.True(context.WasAuthenticatedWith("otp"));
    }

    [Theory]
    [InlineData("OTP")]
    [InlineData("Pwd")]
    [InlineData("mfa")]
    public void WasAuthenticatedWith_CaseOrMethodDiffers_ReturnsFalse(string method)
    {
        var context = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"] };

        Assert.False(context.WasAuthenticatedWith(method));
    }

    [Fact]
    public void Checks_EmptyCollections_ReturnFalse()
    {
        var context = new FakeUserContext();

        Assert.False(context.HasRole("admin"));
        Assert.False(context.HasPermission("orders:write"));
        Assert.False(context.WasAuthenticatedWith("pwd"));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AgeExactlyMaxAge_ReturnsTrue()
    {
        var context = new FakeUserContext { AuthTime = Now.AddMinutes(-5) };

        Assert.True(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AgeOverMaxAge_ReturnsFalse()
    {
        var context = new FakeUserContext { AuthTime = Now.AddMinutes(-5).AddTicks(-1) };

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), Now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_UnknownAuthTime_ReturnsFalse()
    {
        var context = new FakeUserContext();

        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.MaxValue, Now));
    }

    [Theory]
    [InlineData(-300, 300)]
    [InlineData(-301, 300)]
    [InlineData(-10, 0)]
    public void IsAuthenticationFresherThan_SameInputs_MatchesRealUserContext(int authOffsetSeconds, int maxAgeSeconds)
    {
        var authTime = Now.AddSeconds(authOffsetSeconds);
        var maxAge = TimeSpan.FromSeconds(maxAgeSeconds);
        var fake = new FakeUserContext { AuthTime = authTime };
        var real = new UserContext(ActorKind.User, FakeUserContext.DefaultSubjectId) { AuthTime = authTime };

        Assert.Equal(real.IsAuthenticationFresherThan(maxAge, Now), fake.IsAuthenticationFresherThan(maxAge, Now));
    }

    // ---- X1: when each authentication method was verified ----

    [Fact]
    public void AuthenticationMethodTimes_DefaultsToEmpty()
    {
        Assert.Empty(new FakeUserContext().AuthenticationMethodTimes);
    }

    [Fact]
    public void GetAuthenticationMethodTime_ListedMethodWithATime_ReturnsItsTime()
    {
        var context = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"], AuthTime = Now.AddHours(-1) }
            .WithAuthenticationMethodTime("otp", Now.AddMinutes(-2));

        Assert.Equal(Now.AddMinutes(-2), context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_ListedMethodWithoutATime_ReturnsAuthTime()
    {
        var context = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"], AuthTime = Now.AddHours(-1) }
            .WithAuthenticationMethodTime("otp", Now.AddMinutes(-2));

        Assert.Equal(Now.AddHours(-1), context.GetAuthenticationMethodTime("pwd"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_MethodNotListed_ReturnsNull_EvenWithATime()
    {
        var context = new FakeUserContext { AuthenticationMethods = ["pwd"], AuthTime = Now }
            .WithAuthenticationMethodTime("otp", Now);

        Assert.Null(context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_ComparesOrdinally_WhateverTheDictionaryComparer()
    {
        var context = new FakeUserContext
        {
            AuthenticationMethods = ["otp", "OTP"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase) { ["otp"] = Now },
            AuthTime = Now.AddHours(-1),
        };

        Assert.Equal(Now, context.GetAuthenticationMethodTime("otp"));
        Assert.Equal(Now.AddHours(-1), context.GetAuthenticationMethodTime("OTP"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_NullMethod_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FakeUserContext().GetAuthenticationMethodTime(null!));
    }

    [Fact]
    public void GetAuthenticationMethodTime_ThroughTheInterface_UsesTheFakesAnswer()
    {
        IUserContext context = new FakeUserContext { AuthenticationMethods = ["otp"] }.WithAuthenticationMethodTime("otp", Now);

        Assert.Equal(Now, context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void WithAuthenticationMethodTime_ReplacesTheMethodsTime_KeepsOthers_AndReturnsTheSameContext()
    {
        var supplied = new Dictionary<string, DateTimeOffset> { ["hwk"] = Now.AddDays(-1) };
        var context = new FakeUserContext { AuthenticationMethodTimes = supplied };

        var returned = context
            .WithAuthenticationMethodTime("otp", Now.AddMinutes(-9))
            .WithAuthenticationMethodTime("otp", Now.AddMinutes(-1));

        Assert.Same(context, returned);
        Assert.Equal(Now.AddMinutes(-1), context.AuthenticationMethodTimes["otp"]);
        Assert.Equal(Now.AddDays(-1), context.AuthenticationMethodTimes["hwk"]);
        Assert.Single(supplied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithAuthenticationMethodTime_NullOrWhitespaceMethod_Throws(string? method)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FakeUserContext().WithAuthenticationMethodTime(method!, Now));
    }

    [Theory]
    [InlineData("otp")]
    [InlineData("pwd")]
    [InlineData("hwk")]
    [InlineData("OTP")]
    public void GetAuthenticationMethodTime_SameInputs_MatchesRealUserContext(string method)
    {
        var times = new Dictionary<string, DateTimeOffset> { ["otp"] = Now.AddMinutes(-2), ["hwk"] = Now.AddMinutes(-7) };
        var fake = new FakeUserContext { AuthenticationMethods = ["pwd", "otp"], AuthenticationMethodTimes = times, AuthTime = Now.AddHours(-1) };
        var real = new UserContext(ActorKind.User, FakeUserContext.DefaultSubjectId)
        {
            AuthenticationMethods = ["pwd", "otp"],
            AuthenticationMethodTimes = times,
            AuthTime = Now.AddHours(-1),
        };

        Assert.Equal(real.GetAuthenticationMethodTime(method), fake.GetAuthenticationMethodTime(method));
    }
}
