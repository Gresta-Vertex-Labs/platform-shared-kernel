using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.StepUp;

public sealed class TotpStepUpClaimsTransformationTests
{
    private const string Subject = FakeUserContext.DefaultSubjectId;
    private const string Session = "session-1";

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTotpStepUpStore _inner = new();
    private readonly RecordingStepUpStore _store;
    private readonly TotpStepUpOptions _options = new();
    private readonly TestUserContextMapper[] _mappers = [new()];

    public TotpStepUpClaimsTransformationTests() => _store = new RecordingStepUpStore(_inner);

    [Fact]
    public async Task TransformAsync_SessionSteppedUpWithinWindow_AddsOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow.AddMinutes(-5));
        ClaimsPrincipal principal = BuildPrincipal();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        ClaimsIdentity identity = Assert.Single(result.Identities);
        Assert.Contains(identity.Claims, claim => claim is { Type: "amr", Value: "otp" });
        Assert.Contains(identity.Claims, claim => claim is { Type: "amr", Value: "pwd" });
        Assert.True(UserContextResolver.Resolve(result, _mappers).WasAuthenticatedWith("otp"));
    }

    [Fact]
    public async Task TransformAsync_SteppedUp_KeepsIdentityShapeAndDoesNotMutateOriginal()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        ClaimsPrincipal principal = BuildPrincipal();
        ClaimsIdentity original = principal.Identities.Single();
        int originalClaimCount = original.Claims.Count();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        ClaimsIdentity identity = Assert.Single(result.Identities);
        Assert.NotSame(original, identity);
        Assert.Equal(originalClaimCount, original.Claims.Count());
        Assert.False(original.HasClaim("amr", "otp"));
        Assert.True(identity.IsAuthenticated);
        Assert.Equal(original.AuthenticationType, identity.AuthenticationType);
        Assert.Equal(original.NameClaimType, identity.NameClaimType);
        Assert.Equal(original.RoleClaimType, identity.RoleClaimType);
        Assert.Equal(originalClaimCount + 1, identity.Claims.Count());
        Assert.Equal(Subject, identity.FindFirst(SecurityClaimTypes.Subject)?.Value);
    }

    [Fact]
    public async Task TransformAsync_StepUpExactlyAtWindowEdge_AddsOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow - _options.FreshnessWindow);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(BuildPrincipal());

        Assert.True(HasOtp(result));
    }

    [Fact]
    public async Task TransformAsync_StepUpOutsideWindow_DoesNotAddOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow - _options.FreshnessWindow - TimeSpan.FromTicks(1));
        ClaimsPrincipal principal = BuildPrincipal();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.False(HasOtp(result));
    }

    [Fact]
    public async Task TransformAsync_StepUpAgesOut_StopsAddingOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        TotpStepUpClaimsTransformation transformation = CreateTransformation();

        _clock.Advance(TimeSpan.FromMinutes(14));
        bool beforeExpiry = HasOtp(await transformation.TransformAsync(BuildPrincipal()));
        _clock.Advance(TimeSpan.FromMinutes(2));
        bool afterExpiry = HasOtp(await transformation.TransformAsync(BuildPrincipal()));

        Assert.True(beforeExpiry);
        Assert.False(afterExpiry);
    }

    [Fact]
    public async Task TransformAsync_StepUpInFuture_DoesNotAddOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow.AddSeconds(1));

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(BuildPrincipal());

        Assert.False(HasOtp(result));
    }

    [Fact]
    public async Task TransformAsync_StepUpForOtherSessionOfSameUser_DoesNotAddOtpMethod()
    {
        await StepUpAsync(Subject, "session-2", _clock.UtcNow);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(BuildPrincipal(sessionId: Session));

        Assert.False(HasOtp(result));
    }

    [Fact]
    public async Task TransformAsync_StepUpForSameSessionIdOfOtherUser_DoesNotAddOtpMethod()
    {
        await StepUpAsync("other-subject", Session, _clock.UtcNow);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(BuildPrincipal(sessionId: Session));

        Assert.False(HasOtp(result));
    }

    [Fact]
    public async Task TransformAsync_ServicePrincipal_DoesNotAddOtpMethodOrReadStore()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        ClaimsPrincipal principal = new SecurityTestContextBuilder()
            .WithIdentityKind(IdentityKind.ServicePrincipal)
            .WithSessionId(Session)
            .Build();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task TransformAsync_UnauthenticatedPrincipal_DoesNotAddOtpMethodOrReadStore()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        ClaimsPrincipal principal = new SecurityTestContextBuilder().Unauthenticated().WithSessionId(Session).Build();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.False(HasOtp(result));
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task TransformAsync_EmptyPrincipal_ReturnsItUnchanged()
    {
        var principal = new ClaimsPrincipal();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
    }

    [Fact]
    public async Task TransformAsync_AuthenticationTypeWithoutMapper_DoesNotAddOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.Subject, Subject), new Claim(SecurityClaimTypes.SessionId, Session)],
            "ApiKey");
        var principal = new ClaimsPrincipal(identity);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.False(identity.HasClaim("amr", "otp"));
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task TransformAsync_AuthenticationTypeDiffersOnlyByCase_DoesNotAddOtpMethod()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.Subject, Subject), new Claim(SecurityClaimTypes.SessionId, Session)],
            "bearer");

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(new ClaimsPrincipal(identity));

        Assert.False(HasOtp(result));
    }

    [Fact]
    public async Task TransformAsync_UserWithoutSession_DoesNotAddOtpMethodOrReadStore()
    {
        ClaimsPrincipal principal = BuildPrincipal(sessionId: null);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task TransformAsync_NoStepUpRecorded_ReturnsPrincipalUnchanged()
    {
        ClaimsPrincipal principal = BuildPrincipal();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Equal(1, _store.Reads);
    }

    [Fact]
    public async Task TransformAsync_OtpClaimAlreadyPresent_ReturnsSamePrincipalWithoutReadingStore()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        ClaimsPrincipal principal = BuildPrincipal(methods: ["pwd", "otp"]);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Single(result.FindAll("amr"), claim => claim.Value == "otp");
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task TransformAsync_AppliedTwice_AddsOtpMethodOnce()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        TotpStepUpClaimsTransformation transformation = CreateTransformation();

        ClaimsPrincipal once = await transformation.TransformAsync(BuildPrincipal());
        ClaimsPrincipal twice = await transformation.TransformAsync(once);

        Assert.Same(once, twice);
        Assert.Single(twice.FindAll("amr"), claim => claim.Value == "otp");
    }

    [Fact]
    public async Task TransformAsync_MultipleIdentities_StepsUpMappedIdentityAndPreservesOthersInOrder()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        var cookie = new ClaimsIdentity([new Claim(SecurityClaimTypes.Subject, Subject), new Claim(SecurityClaimTypes.SessionId, Session)], "Cookies");
        ClaimsIdentity bearer = BuildPrincipal().Identities.Single();
        var anonymous = new ClaimsIdentity([new Claim("device", "d1")]);
        var principal = new ClaimsPrincipal([cookie, bearer, anonymous]);

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(principal);

        ClaimsIdentity[] identities = [.. result.Identities];
        Assert.Equal(3, identities.Length);
        Assert.Same(cookie, identities[0]);
        Assert.Equal("Bearer", identities[1].AuthenticationType);
        Assert.True(identities[1].HasClaim("amr", "otp"));
        Assert.Same(anonymous, identities[2]);
        Assert.False(cookie.HasClaim("amr", "otp"));
        Assert.False(bearer.HasClaim("amr", "otp"));
    }

    [Fact]
    public async Task TransformAsync_FirstMappedIdentityIsServicePrincipal_DoesNotStepUpLaterUserIdentity()
    {
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        ClaimsIdentity app = new SecurityTestContextBuilder().WithSubjectId("app-1").WithIdentityKind(IdentityKind.ServicePrincipal).Build().Identities.Single();
        ClaimsIdentity user = BuildPrincipal().Identities.Single();

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(new ClaimsPrincipal([app, user]));

        Assert.False(HasOtp(result));
        Assert.False(UserContextResolver.Resolve(result, _mappers).WasAuthenticatedWith("otp"));
    }

    [Fact]
    public async Task TransformAsync_InnerTransformation_RunsFirstAndItsOutputIsStepUpInput()
    {
        await StepUpAsync(Subject, "session-from-inner", _clock.UtcNow);
        var log = new TransformationLog();
        var inner = new SessionAddingTransformation(log, "session-from-inner");
        TotpStepUpClaimsTransformation transformation = CreateTransformation(inner: inner);

        ClaimsPrincipal result = await transformation.TransformAsync(BuildPrincipal(sessionId: null));

        Assert.Equal("inner", Assert.Single(log.Entries));
        Assert.True(HasOtp(result));
        Assert.Equal("session-from-inner", result.FindFirst(SecurityClaimTypes.SessionId)?.Value);
    }

    [Fact]
    public async Task TransformAsync_InnerTransformationWithoutStepUp_ReturnsInnerOutput()
    {
        var log = new TransformationLog();
        var inner = new MarkerClaimsTransformation(log);

        ClaimsPrincipal result = await CreateTransformation(inner: inner).TransformAsync(BuildPrincipal());

        Assert.Equal("type", Assert.Single(log.Entries));
        Assert.True(result.HasClaim(MarkerClaimsTransformation.ClaimType, "type"));
    }

    [Fact]
    public async Task TransformAsync_CustomClaimTypeAndMethod_AddsConfiguredClaim()
    {
        _options.AuthenticationMethodClaimType = "methods";
        _options.AuthenticationMethod = "totp";
        await StepUpAsync(Subject, Session, _clock.UtcNow);
        TotpStepUpClaimsTransformation transformation = CreateTransformation();

        ClaimsPrincipal result = await transformation.TransformAsync(BuildPrincipal());
        ClaimsPrincipal again = await transformation.TransformAsync(result);

        Assert.True(result.HasClaim("methods", "totp"));
        Assert.False(result.HasClaim("amr", "otp"));
        Assert.Same(result, again);
    }

    [Fact]
    public async Task TransformAsync_CustomFreshnessWindow_IsApplied()
    {
        _options.FreshnessWindow = TimeSpan.FromMinutes(2);
        await StepUpAsync(Subject, Session, _clock.UtcNow.AddMinutes(-3));

        ClaimsPrincipal result = await CreateTransformation().TransformAsync(BuildPrincipal());

        Assert.False(HasOtp(result));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        IOptions<TotpStepUpOptions> options = Options.Create(_options);

        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(null!, _store, _clock, options));
        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(_mappers, null!, _clock, options));
        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(_mappers, _store, null!, options));
        Assert.Throws<ArgumentNullException>(() => new TotpStepUpClaimsTransformation(_mappers, _store, _clock, null!));
    }

    [Fact]
    public async Task TransformAsync_NullPrincipal_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateTransformation().TransformAsync(null!));
    }

    private TotpStepUpClaimsTransformation CreateTransformation(IClaimsTransformation? inner = null) =>
        new(_mappers, _store, _clock, Options.Create(_options), inner);

    private ValueTask StepUpAsync(string subjectId, string sessionId, DateTimeOffset verifiedAt) =>
        _inner.RecordAsync(subjectId, sessionId, verifiedAt, verifiedAt + _options.FreshnessWindow, CancellationToken.None);

    private static ClaimsPrincipal BuildPrincipal(string? sessionId = Session, string[]? methods = null) =>
        new SecurityTestContextBuilder()
            .WithSessionId(sessionId)
            .WithAuthenticationMethods(methods ?? ["pwd"])
            .Build();

    private static bool HasOtp(ClaimsPrincipal principal) => principal.HasClaim("amr", "otp");

    private sealed class SessionAddingTransformation(TransformationLog log, string sessionId) : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            log.Add("inner");
            return Task.FromResult(new ClaimsPrincipal(principal.Identities.Select(identity =>
                new ClaimsIdentity(identity, [new Claim(SecurityClaimTypes.SessionId, sessionId)]))));
        }
    }
}
