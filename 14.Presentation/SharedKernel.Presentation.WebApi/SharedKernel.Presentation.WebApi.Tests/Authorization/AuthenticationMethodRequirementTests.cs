using FluentAssertions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Security.Abstractions;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// X1: an authentication-method requirement with a maximum age holds only while one of its methods was verified no
/// longer than that ago (<see cref="IUserContext.GetAuthenticationMethodTime"/> against the clock). Without a maximum
/// age it is unchanged: the method only has to be present, and the clock is never read.
/// </summary>
public sealed class AuthenticationMethodRequirementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void WithoutMaxAge_PresentMethod_Holds_WithoutReadingTheClock()
    {
        var requirement = new AuthenticationMethodRequirement(["otp"]);

        requirement.IsSatisfiedBy(User(("otp", Now.AddDays(-30))), ClockMustNotBeRead).Should().BeTrue();
        requirement.IsSatisfiedBy(User(methods: ["otp"]), ClockMustNotBeRead).Should().BeTrue();
    }

    [Fact]
    public void WithoutMaxAge_MissingMethod_DoesNotHold()
    {
        new AuthenticationMethodRequirement(["otp"]).IsSatisfiedBy(User(methods: ["pwd"]), ClockMustNotBeRead).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(300)]
    public void WithMaxAge_MethodVerifiedWithinIt_Holds(int secondsAgo)
    {
        var requirement = new AuthenticationMethodRequirement(["otp"], FiveMinutes);

        requirement.IsSatisfiedBy(User(("otp", Now.AddSeconds(-secondsAgo))), () => Now).Should().BeTrue();
    }

    [Theory]
    [InlineData(301)]
    [InlineData(3600)]
    public void WithMaxAge_MethodVerifiedLongerAgo_DoesNotHold(int secondsAgo)
    {
        var requirement = new AuthenticationMethodRequirement(["otp"], FiveMinutes);

        requirement.IsSatisfiedBy(User(("otp", Now.AddSeconds(-secondsAgo))), () => Now).Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_SameCallerLosesTheMethod_AsTheClockMoves()
    {
        // The principal of a long-lived connection never changes; only the clock does.
        var requirement = new AuthenticationMethodRequirement(["otp"], FiveMinutes);
        IUserContext connection = User(("otp", Now));
        var clock = Now;

        var atStart = requirement.IsSatisfiedBy(connection, () => clock);
        clock = Now.AddMinutes(4);
        var fourMinutesIn = requirement.IsSatisfiedBy(connection, () => clock);
        clock = Now.AddMinutes(6);
        var sixMinutesIn = requirement.IsSatisfiedBy(connection, () => clock);

        atStart.Should().BeTrue();
        fourMinutesIn.Should().BeTrue();
        sixMinutesIn.Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_MethodWithoutAnyTime_DoesNotHold()
    {
        new AuthenticationMethodRequirement(["otp"], FiveMinutes).IsSatisfiedBy(User(methods: ["otp"]), () => Now).Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_MethodCarriedByTheCredential_DatesFromTheSignIn()
    {
        var requirement = new AuthenticationMethodRequirement(["mfa"], FiveMinutes);

        requirement.IsSatisfiedBy(User(methods: ["mfa"], authTime: Now.AddMinutes(-2)), () => Now).Should().BeTrue();
        requirement.IsSatisfiedBy(User(methods: ["mfa"], authTime: Now.AddMinutes(-20)), () => Now).Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_RecentTimeForAMethodTheCallerDoesNotHave_DoesNotHold()
    {
        var caller = new UserContext(IdentityKind.User, "user-1")
        {
            AuthenticationMethods = ["pwd"],
            AuthenticationMethodTimes = new Dictionary<string, DateTimeOffset> { ["otp"] = Now },
        };

        new AuthenticationMethodRequirement(["otp"], FiveMinutes).IsSatisfiedBy(caller, () => Now).Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_TimeWithinClockSkew_Holds_ButFarInTheFuture_DoesNot()
    {
        // Mirrors IsAuthenticationFresherThan: a skewed clock is tolerated up to UserContext.MaxFutureAuthTime, a
        // forged or badly skewed time never passes.
        var requirement = new AuthenticationMethodRequirement(["otp"], FiveMinutes);

        requirement.IsSatisfiedBy(User(("otp", Now.Add(UserContext.MaxFutureAuthTime))), () => Now).Should().BeTrue();
        requirement.IsSatisfiedBy(User(("otp", Now.Add(UserContext.MaxFutureAuthTime).AddSeconds(1))), () => Now).Should().BeFalse();
        requirement.IsSatisfiedBy(User(("otp", Now.AddYears(1))), () => Now).Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_AnyRecentMethodOfTheList_Suffices()
    {
        var requirement = new AuthenticationMethodRequirement(["otp", "hwk"], FiveMinutes);

        requirement.IsSatisfiedBy(User(("otp", Now.AddHours(-1)), ("hwk", Now.AddMinutes(-1))), () => Now).Should().BeTrue();
        requirement.IsSatisfiedBy(User(("otp", Now.AddHours(-1)), ("hwk", Now.AddHours(-2))), () => Now).Should().BeFalse();
    }

    [Fact]
    public void WithMaxAge_ContextThatDoesNotReportTimes_DoesNotHold()
    {
        // An IUserContext that does not implement GetAuthenticationMethodTime gets the default, null: its callers are
        // refused rather than passed.
        IUserContext caller = new ContextWithoutMethodTimes();

        new AuthenticationMethodRequirement(["otp"], FiveMinutes).IsSatisfiedBy(caller, () => Now).Should().BeFalse();
        new AuthenticationMethodRequirement(["otp"]).IsSatisfiedBy(caller, () => Now).Should().BeTrue();
    }

    private static DateTimeOffset ClockMustNotBeRead() => throw new InvalidOperationException("The clock was read.");

    private static UserContext User(params (string Method, DateTimeOffset VerifiedAt)[] times) =>
        new(IdentityKind.User, "user-1")
        {
            AuthenticationMethods = [.. times.Select(time => time.Method)],
            AuthenticationMethodTimes = times.ToDictionary(time => time.Method, time => time.VerifiedAt),
        };

    private static UserContext User(string[] methods, DateTimeOffset? authTime = null) =>
        new(IdentityKind.User, "user-1") { AuthenticationMethods = methods, AuthTime = authTime };

    // Written before GetAuthenticationMethodTime existed: it has the method and a sign-in time, but reports no method time.
    private sealed class ContextWithoutMethodTimes : IUserContext
    {
        public IdentityKind IdentityKind => IdentityKind.User;

        public bool IsAuthenticated => true;

        public string? SubjectId => "user-1";

        public string? ClientId => null;

        public Guid? TenantId => null;

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
