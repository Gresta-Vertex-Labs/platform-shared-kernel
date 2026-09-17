using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Enrollment;

public sealed class TotpEnrollmentServiceConfirmTests
{
    private const int ChallengeNotAcceptedEventId = 12400;

    private readonly TotpTestHarness _harness = new();

    [Fact]
    public async Task ConfirmAsync_CurrentCodeForEnrolledSecret_ReturnsVerified()
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();
        TotpEnrollment enrollment = service.Create("Contoso", "alice");

        TotpChallengeResult result = await service.ConfirmAsync(
            TotpTestHarness.User(), enrollment.Secret, _harness.CurrentCode(enrollment.Secret), enrollment.Parameters);

        Assert.Equal(TotpChallengeResult.Verified, result);
        Assert.DoesNotContain(_harness.EnrollmentLogger.Records, record => record.EventId.Id == ChallengeNotAcceptedEventId);
    }

    [Fact]
    public async Task ConfirmAsync_CustomParameters_VerifiesCodeGeneratedWithThoseParameters()
    {
        var parameters = new TotpParameters { Digits = 8, StepSeconds = 60, Algorithm = HotpAlgorithm.Sha512 };
        TotpEnrollmentService service = _harness.CreateEnrollmentService();
        TotpEnrollment enrollment = service.Create("Contoso", "alice", parameters);

        TotpChallengeResult withDefaults = await service.ConfirmAsync(
            TotpTestHarness.User(), enrollment.Secret, _harness.CurrentCode(enrollment.Secret), parameters);
        TotpChallengeResult withParameters = await service.ConfirmAsync(
            TotpTestHarness.User(), enrollment.Secret, _harness.CurrentCode(enrollment.Secret, parameters), parameters);

        Assert.Equal(TotpChallengeResult.Invalid, withDefaults);
        Assert.Equal(TotpChallengeResult.Verified, withParameters);
    }

    [Fact]
    public async Task ConfirmAsync_WrongCode_ReturnsInvalidAndLogs()
    {
        byte[] secret = TotpTestHarness.NewSecret();
        TotpEnrollmentService service = _harness.CreateEnrollmentService();
        string code = _harness.WrongCode(secret);

        TotpChallengeResult result = await service.ConfirmAsync(TotpTestHarness.User(), secret, code);

        Assert.Equal(TotpChallengeResult.Invalid, result);
        var record = Assert.Single(_harness.EnrollmentLogger.Records, record => record.EventId.Id == ChallengeNotAcceptedEventId);
        Assert.True(record.TryGetProperty("Result", out object? logged));
        Assert.Equal(TotpChallengeResult.Invalid, logged);
        Assert.True(record.TryGetProperty("Operation", out object? operation));
        Assert.Equal("ConfirmEnrollment", operation);
        Assert.DoesNotContain(code, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmAsync_SameTimeStepTwice_ReturnsReplayed()
    {
        byte[] secret = TotpTestHarness.NewSecret();
        TotpEnrollmentService service = _harness.CreateEnrollmentService();
        string code = _harness.CurrentCode(secret);

        TotpChallengeResult first = await service.ConfirmAsync(TotpTestHarness.User(), secret, code);
        TotpChallengeResult second = await service.ConfirmAsync(TotpTestHarness.User(), secret, code);

        Assert.Equal(TotpChallengeResult.Verified, first);
        Assert.Equal(TotpChallengeResult.Replayed, second);
    }

    [Fact]
    public async Task ConfirmAsync_AcceptedCode_CannotBeReusedForChallenge()
    {
        byte[] secret = TotpTestHarness.NewSecret();
        string code = _harness.CurrentCode(secret);
        await _harness.CreateEnrollmentService().ConfirmAsync(TotpTestHarness.User(), secret, code);

        TotpChallengeResult challenge = await _harness.CreateChallengeService().VerifyCodeAsync(TotpTestHarness.User(), secret, code);

        Assert.Equal(TotpChallengeResult.Replayed, challenge);
    }

    [Fact]
    public async Task ConfirmAsync_Verified_DoesNotRecordStepUp()
    {
        byte[] secret = TotpTestHarness.NewSecret();

        await _harness.CreateEnrollmentService().ConfirmAsync(TotpTestHarness.User(), secret, _harness.CurrentCode(secret));

        Assert.Null(await _harness.StepUps.GetLastVerifiedAsync(TotpTestHarness.SubjectId, "session-1", CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmAsync_Throttled_ReturnsThrottledWithoutVerifying()
    {
        byte[] secret = TotpTestHarness.NewSecret();
        var throttle = new RecordingAttemptThrottle { Throttled = true };
        var verifier = new CountingVerifier(_harness.CreateVerifier());
        TotpEnrollmentService service = _harness.CreateEnrollmentService(throttle: throttle, verifier: verifier);

        TotpChallengeResult result = await service.ConfirmAsync(TotpTestHarness.User(), secret, _harness.CurrentCode(secret));

        Assert.Equal(TotpChallengeResult.Throttled, result);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal(TotpTestHarness.SubjectId, Assert.Single(throttle.Checks));
        Assert.Empty(throttle.Attempts);
        Assert.Contains(_harness.EnrollmentLogger.Records, record => record.EventId.Id == ChallengeNotAcceptedEventId);
    }

    [Fact]
    public async Task ConfirmAsync_EveryAttempt_IsRecordedOnThrottleForSubject()
    {
        byte[] secret = TotpTestHarness.NewSecret();
        var throttle = new RecordingAttemptThrottle();
        TotpEnrollmentService service = _harness.CreateEnrollmentService(throttle: throttle);
        string code = _harness.CurrentCode(secret);

        Assert.Equal(TotpChallengeResult.Invalid, await service.ConfirmAsync(TotpTestHarness.User(), secret, _harness.WrongCode(secret)));
        Assert.Equal(TotpChallengeResult.Verified, await service.ConfirmAsync(TotpTestHarness.User(), secret, code));
        Assert.Equal(TotpChallengeResult.Replayed, await service.ConfirmAsync(TotpTestHarness.User(), secret, code));

        Assert.Equal(3, throttle.Attempts.Count);
        Assert.All(throttle.Attempts, key => Assert.Equal(TotpTestHarness.SubjectId, key));
    }

    public static TheoryData<string> NonUserCallers => ["ServicePrincipal", "Anonymous", "System", "UserWithoutSubject"];

    [Theory]
    [MemberData(nameof(NonUserCallers))]
    public async Task ConfirmAsync_CallerIsNotUser_ReturnsNoSessionWithoutVerifying(string caller)
    {
        byte[] secret = TotpTestHarness.NewSecret();
        var throttle = new RecordingAttemptThrottle();
        var verifier = new CountingVerifier(_harness.CreateVerifier());
        TotpEnrollmentService service = _harness.CreateEnrollmentService(throttle: throttle, verifier: verifier);

        TotpChallengeResult result = await service.ConfirmAsync(CreateCaller(caller), secret, _harness.CurrentCode(secret));

        Assert.Equal(TotpChallengeResult.NoSession, result);
        Assert.Equal(0, verifier.Calls);
        Assert.Empty(throttle.Checks);
    }

    [Fact]
    public async Task ConfirmAsync_UserWithoutSessionId_IsStillVerified()
    {
        byte[] secret = TotpTestHarness.NewSecret();

        TotpChallengeResult result = await _harness.CreateEnrollmentService()
            .ConfirmAsync(TotpTestHarness.User(sessionId: null), secret, _harness.CurrentCode(secret));

        Assert.Equal(TotpChallengeResult.Verified, result);
    }

    [Fact]
    public async Task ConfirmAsync_WithoutThrottle_Verifies()
    {
        byte[] secret = TotpTestHarness.NewSecret();

        TotpChallengeResult result = await _harness.CreateEnrollmentService(throttle: null)
            .ConfirmAsync(TotpTestHarness.User(), secret, _harness.CurrentCode(secret));

        Assert.Equal(TotpChallengeResult.Verified, result);
    }

    [Fact]
    public async Task ConfirmAsync_NullArguments_Throw()
    {
        TotpEnrollmentService service = _harness.CreateEnrollmentService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConfirmAsync(null!, TotpTestHarness.NewSecret(), "123456").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConfirmAsync(TotpTestHarness.User(), TotpTestHarness.NewSecret(), null!).AsTask());
    }

    private static IUserContext CreateCaller(string caller) => caller switch
    {
        "ServicePrincipal" => new FakeUserContext { IdentityKind = IdentityKind.ServicePrincipal, SessionId = "session-1" },
        "Anonymous" => AnonymousUserContext.Instance,
        "System" => SystemUserContext.Instance,
        _ => new FakeUserContext { SubjectId = null, SessionId = "session-1" },
    };
}
