using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Challenge;

public sealed class TotpChallengeServiceVerifyCodeTests
{
    private const int ChallengeNotAcceptedEventId = 12400;
    private const int StepUpRecordedEventId = 12401;

    private readonly TotpTestHarness _harness = new();
    private readonly byte[] _secret = TotpTestHarness.NewSecret();

    [Fact]
    public async Task VerifyCodeAsync_CurrentCode_RecordsStepUpForSubjectAndSession()
    {
        var stepUps = new RecordingStepUpStore(_harness.StepUps);
        _harness.StepUpOptions.FreshnessWindow = TimeSpan.FromMinutes(7);
        TotpChallengeService service = _harness.CreateChallengeService(stepUps: stepUps);

        TotpChallengeResult result = await service.VerifyCodeAsync(TotpTestHarness.User("session-A"), _secret, _harness.CurrentCode(_secret));

        Assert.Equal(TotpChallengeResult.Verified, result);
        var record = Assert.Single(stepUps.Records);
        Assert.Equal(TotpTestHarness.SubjectId, record.SubjectId);
        Assert.Equal("session-A", record.SessionId);
        Assert.Equal(_harness.Clock.UtcNow, record.VerifiedAt);
        Assert.Equal(_harness.Clock.UtcNow + TimeSpan.FromMinutes(7), record.ExpiresAt);
    }

    [Fact]
    public async Task VerifyCodeAsync_CodeWithinDriftWindow_IsVerified()
    {
        string previousStepCode = new TotpGenerator(_harness.Clock).GenerateCode(_secret, _harness.Clock.UtcNow.AddSeconds(-30));

        TotpChallengeResult result = await _harness.CreateChallengeService().VerifyCodeAsync(TotpTestHarness.User(), _secret, previousStepCode);

        Assert.Equal(TotpChallengeResult.Verified, result);
    }

    [Fact]
    public async Task VerifyCodeAsync_CodeOutsideDriftWindow_IsInvalid()
    {
        string oldCode = new TotpGenerator(_harness.Clock).GenerateCode(_secret, _harness.Clock.UtcNow.AddMinutes(-5));
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult result = await _harness.CreateChallengeService(stepUps: stepUps)
            .VerifyCodeAsync(TotpTestHarness.User(), _secret, oldCode);

        Assert.Equal(TotpChallengeResult.Invalid, result);
        Assert.Empty(stepUps.Records);
    }

    [Fact]
    public async Task VerifyCodeAsync_WrongCode_IsInvalidAndRecordsNothing()
    {
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult result = await _harness.CreateChallengeService(stepUps: stepUps)
            .VerifyCodeAsync(TotpTestHarness.User(), _secret, _harness.WrongCode(_secret));

        Assert.Equal(TotpChallengeResult.Invalid, result);
        Assert.Empty(stepUps.Records);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData("12345")]
    [InlineData("1234567")]
    public async Task VerifyCodeAsync_MalformedCode_IsInvalid(string code)
    {
        TotpChallengeResult result = await _harness.CreateChallengeService().VerifyCodeAsync(TotpTestHarness.User(), _secret, code);

        Assert.Equal(TotpChallengeResult.Invalid, result);
    }

    [Fact]
    public async Task VerifyCodeAsync_SameCodeTwice_SecondIsReplayedAndRecordsNothing()
    {
        string code = _harness.CurrentCode(_secret);
        TotpChallengeResult first = await _harness.CreateChallengeService().VerifyCodeAsync(TotpTestHarness.User(), _secret, code);
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult second = await _harness.CreateChallengeService(stepUps: stepUps).VerifyCodeAsync(TotpTestHarness.User(), _secret, code);

        Assert.Equal(TotpChallengeResult.Verified, first);
        Assert.Equal(TotpChallengeResult.Replayed, second);
        Assert.Empty(stepUps.Records);
    }

    [Fact]
    public async Task VerifyCodeAsync_SameCodeInSecondSessionOfSameSubject_IsReplayed()
    {
        TotpChallengeService service = _harness.CreateChallengeService();
        string code = _harness.CurrentCode(_secret);

        TotpChallengeResult first = await service.VerifyCodeAsync(TotpTestHarness.User("session-1"), _secret, code);
        TotpChallengeResult second = await service.VerifyCodeAsync(TotpTestHarness.User("session-2"), _secret, code);

        Assert.Equal(TotpChallengeResult.Verified, first);
        Assert.Equal(TotpChallengeResult.Replayed, second);
        Assert.NotNull(await _harness.StepUps.GetLastVerifiedAsync(TotpTestHarness.SubjectId, "session-1", CancellationToken.None));
        Assert.Null(await _harness.StepUps.GetLastVerifiedAsync(TotpTestHarness.SubjectId, "session-2", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyCodeAsync_SameCodeForDifferentSubject_IsNotReplayed()
    {
        TotpChallengeService service = _harness.CreateChallengeService();
        string code = _harness.CurrentCode(_secret);

        TotpChallengeResult first = await service.VerifyCodeAsync(TotpTestHarness.User("session-1", "subject-a"), _secret, code);
        TotpChallengeResult second = await service.VerifyCodeAsync(TotpTestHarness.User("session-1", "subject-b"), _secret, code);

        Assert.Equal(TotpChallengeResult.Verified, first);
        Assert.Equal(TotpChallengeResult.Verified, second);
        Assert.NotNull(await _harness.StepUps.GetLastVerifiedAsync("subject-b", "session-1", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyCodeAsync_Throttled_ReturnsThrottledWithoutVerifyingOrRecording()
    {
        var throttle = new RecordingAttemptThrottle { Throttled = true };
        var verifier = new CountingVerifier(_harness.CreateVerifier());
        var stepUps = new RecordingStepUpStore(_harness.StepUps);
        TotpChallengeService service = _harness.CreateChallengeService(throttle, verifier, stepUps);
        string code = _harness.CurrentCode(_secret);

        TotpChallengeResult result = await service.VerifyCodeAsync(TotpTestHarness.User(), _secret, code);

        Assert.Equal(TotpChallengeResult.Throttled, result);
        Assert.Equal(0, verifier.Calls);
        Assert.Empty(stepUps.Records);
        Assert.Equal(TotpTestHarness.SubjectId, Assert.Single(throttle.Checks));

        throttle.Throttled = false;
        Assert.Equal(TotpChallengeResult.Verified, await service.VerifyCodeAsync(TotpTestHarness.User(), _secret, code));
    }

    [Fact]
    public async Task VerifyCodeAsync_EveryCheckedAttempt_IsRecordedOnThrottleForSubject()
    {
        var throttle = new RecordingAttemptThrottle();
        TotpChallengeService service = _harness.CreateChallengeService(throttle);
        string code = _harness.CurrentCode(_secret);

        await service.VerifyCodeAsync(TotpTestHarness.User(), _secret, _harness.WrongCode(_secret));
        await service.VerifyCodeAsync(TotpTestHarness.User(), _secret, code);
        await service.VerifyCodeAsync(TotpTestHarness.User("session-2"), _secret, code);

        Assert.Equal(3, throttle.Attempts.Count);
        Assert.All(throttle.Attempts, key => Assert.Equal(TotpTestHarness.SubjectId, key));
    }

    public static TheoryData<string> CallersWithoutSession => ["ServicePrincipal", "Anonymous", "System", "UserWithoutSession", "UserWithoutSubject"];

    [Theory]
    [MemberData(nameof(CallersWithoutSession))]
    public async Task VerifyCodeAsync_CallerWithoutUserSession_ReturnsNoSessionWithoutCheckingCode(string caller)
    {
        var throttle = new RecordingAttemptThrottle();
        var verifier = new CountingVerifier(_harness.CreateVerifier());
        var stepUps = new RecordingStepUpStore(_harness.StepUps);
        TotpChallengeService service = _harness.CreateChallengeService(throttle, verifier, stepUps);

        TotpChallengeResult result = await service.VerifyCodeAsync(CreateCaller(caller), _secret, _harness.CurrentCode(_secret));

        Assert.Equal(TotpChallengeResult.NoSession, result);
        Assert.Equal(0, verifier.Calls);
        Assert.Empty(throttle.Checks);
        Assert.Empty(stepUps.Records);
        Assert.Contains(_harness.ChallengeLogger.Records, record => record.EventId.Id == ChallengeNotAcceptedEventId);
    }

    [Fact]
    public async Task VerifyCodeAsync_WithoutThrottle_Verifies()
    {
        TotpChallengeResult result = await _harness.CreateChallengeService(throttle: null)
            .VerifyCodeAsync(TotpTestHarness.User(), _secret, _harness.CurrentCode(_secret));

        Assert.Equal(TotpChallengeResult.Verified, result);
    }

    [Fact]
    public async Task VerifyCodeAsync_Verified_LogsStepUpRecordedWithoutCode()
    {
        string code = _harness.CurrentCode(_secret);

        await _harness.CreateChallengeService().VerifyCodeAsync(TotpTestHarness.User(), _secret, code);

        var record = Assert.Single(_harness.ChallengeLogger.Records);
        Assert.Equal(StepUpRecordedEventId, record.EventId.Id);
        Assert.True(record.TryGetProperty("Operation", out object? operation));
        Assert.Equal("Code", operation);
        AssertNoSecretsLogged(_harness.ChallengeLogger.Records, code);
    }

    [Theory]
    [InlineData(TotpChallengeResult.Invalid)]
    [InlineData(TotpChallengeResult.Replayed)]
    [InlineData(TotpChallengeResult.Throttled)]
    public async Task VerifyCodeAsync_NotAccepted_LogsResultWithoutCode(TotpChallengeResult expected)
    {
        string code = expected == TotpChallengeResult.Invalid ? _harness.WrongCode(_secret) : _harness.CurrentCode(_secret);
        var throttle = new RecordingAttemptThrottle { Throttled = expected == TotpChallengeResult.Throttled };
        TotpChallengeService service = _harness.CreateChallengeService(throttle);
        if (expected == TotpChallengeResult.Replayed)
        {
            await service.VerifyCodeAsync(TotpTestHarness.User(), _secret, code);
            _harness.ChallengeLogger.Clear();
        }

        TotpChallengeResult result = await service.VerifyCodeAsync(TotpTestHarness.User(), _secret, code);

        Assert.Equal(expected, result);
        var record = Assert.Single(_harness.ChallengeLogger.Records);
        Assert.Equal(ChallengeNotAcceptedEventId, record.EventId.Id);
        Assert.True(record.TryGetProperty("Result", out object? logged));
        Assert.Equal(expected, logged);
        AssertNoSecretsLogged(_harness.ChallengeLogger.Records, code);
    }

    [Fact]
    public async Task VerifyCodeAsync_NullArguments_Throw()
    {
        TotpChallengeService service = _harness.CreateChallengeService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.VerifyCodeAsync(null!, _secret, "123456").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.VerifyCodeAsync(TotpTestHarness.User(), _secret, null!).AsTask());
    }

    private void AssertNoSecretsLogged(IReadOnlyList<LogRecord> records, string code)
    {
        string secretBase32 = Base32.Encode(_secret);
        foreach (LogRecord record in records)
        {
            Assert.DoesNotContain(code, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secretBase32, record.Message, StringComparison.Ordinal);
            Assert.All(record.State ?? [], property =>
                Assert.DoesNotContain(code, Convert.ToString(property.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal));
        }
    }

    private static IUserContext CreateCaller(string caller) => caller switch
    {
        "ServicePrincipal" => new FakeUserContext { IdentityKind = IdentityKind.ServicePrincipal, SessionId = "session-1" },
        "Anonymous" => AnonymousUserContext.Instance,
        "System" => SystemUserContext.Instance,
        "UserWithoutSession" => TotpTestHarness.User(sessionId: null),
        _ => new FakeUserContext { SubjectId = null, SessionId = "session-1" },
    };
}
