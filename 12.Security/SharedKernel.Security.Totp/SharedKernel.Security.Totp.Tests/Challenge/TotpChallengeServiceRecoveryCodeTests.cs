using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp.Tests.TestDoubles;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Challenge;

public sealed class TotpChallengeServiceRecoveryCodeTests
{
    private const int ChallengeNotAcceptedEventId = 12400;
    private const int StepUpRecordedEventId = 12401;
    private const int RecoveryCodeRedeemedEventId = 12402;

    private readonly TotpTestHarness _harness = new();

    [Fact]
    public async Task RedeemRecoveryCodeAsync_UnusedCode_VerifiesOnceAndSecondUseIsInvalid()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        TotpChallengeService service = _harness.CreateChallengeService();
        string code = enrollment.RecoveryCodes[3];

        TotpChallengeResult first = await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), code);
        TotpChallengeResult second = await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), code);

        Assert.Equal(TotpChallengeResult.Verified, first);
        Assert.Equal(TotpChallengeResult.Invalid, second);
        IReadOnlyList<StoredRecoveryCode> unused = await _harness.RecoveryCodes.GetUnusedAsync(TotpTestHarness.SubjectId, CancellationToken.None);
        Assert.Equal(9, unused.Count);
        Assert.DoesNotContain(unused, stored => stored.Id == enrollment.StoredRecoveryCodes[3].Id);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_Verified_RecordsStepUpForSession()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        var stepUps = new RecordingStepUpStore(_harness.StepUps);
        _harness.StepUpOptions.FreshnessWindow = TimeSpan.FromMinutes(20);

        TotpChallengeResult result = await _harness.CreateChallengeService(stepUps: stepUps)
            .RedeemRecoveryCodeAsync(TotpTestHarness.User("session-R"), enrollment.RecoveryCodes[0]);

        Assert.Equal(TotpChallengeResult.Verified, result);
        var record = Assert.Single(stepUps.Records);
        Assert.Equal((TotpTestHarness.SubjectId, "session-R"), (record.SubjectId, record.SessionId));
        Assert.Equal(_harness.Clock.UtcNow, record.VerifiedAt);
        Assert.Equal(_harness.Clock.UtcNow + TimeSpan.FromMinutes(20), record.ExpiresAt);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_FormattingVariations_AreAccepted()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        TotpChallengeService service = _harness.CreateChallengeService();
        string Raw(int index) => enrollment.RecoveryCodes[index].Replace("-", string.Empty, StringComparison.Ordinal);
        string[] variants =
        [
            enrollment.RecoveryCodes[0].ToLowerInvariant(),
            Raw(1),
            Raw(2).ToLowerInvariant(),
            $"  {Raw(3)[..5]} {Raw(3)[5..]}  ",
            $"{Raw(4)[..2]}-{Raw(4)[2..7]}-{Raw(4)[7..]}",
            $"{Raw(5)[..5].ToLowerInvariant()}-{Raw(5)[5..]}",
        ];

        foreach (string variant in variants)
        {
            Assert.Equal(TotpChallengeResult.Verified, await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), variant));
        }

        Assert.Equal(4, (await _harness.RecoveryCodes.GetUnusedAsync(TotpTestHarness.SubjectId, CancellationToken.None)).Count);
    }

    [Theory]
    [InlineData("AAAAA-AAAAA")]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("---")]
    public async Task RedeemRecoveryCodeAsync_UnknownCode_IsInvalidAndConsumesNothing(string code)
    {
        _harness.EnrollAndSaveRecoveryCodes();
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult result = await _harness.CreateChallengeService(stepUps: stepUps).RedeemRecoveryCodeAsync(TotpTestHarness.User(), code);

        Assert.Equal(TotpChallengeResult.Invalid, result);
        Assert.Empty(stepUps.Records);
        Assert.Equal(10, (await _harness.RecoveryCodes.GetUnusedAsync(TotpTestHarness.SubjectId, CancellationToken.None)).Count);
        Assert.Contains(_harness.ChallengeLogger.Records, record => record.EventId.Id == ChallengeNotAcceptedEventId);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_CodeWithOneCharacterChanged_IsInvalid()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes(recoveryCodeCount: 1);
        string code = enrollment.RecoveryCodes[0];
        char last = code[^1];
        string tampered = code[..^1] + (last == 'A' ? 'B' : 'A');

        TotpChallengeResult result = await _harness.CreateChallengeService().RedeemRecoveryCodeAsync(TotpTestHarness.User(), tampered);

        Assert.Equal(TotpChallengeResult.Invalid, result);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_CodeOfAnotherSubject_IsInvalidAndLeavesTheirCodeUnused()
    {
        TotpEnrollment otherEnrollment = _harness.EnrollAndSaveRecoveryCodes(subjectId: "other-subject");
        _harness.EnrollAndSaveRecoveryCodes(subjectId: TotpTestHarness.SubjectId);
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult result = await _harness.CreateChallengeService(stepUps: stepUps)
            .RedeemRecoveryCodeAsync(TotpTestHarness.User(), otherEnrollment.RecoveryCodes[0]);

        Assert.Equal(TotpChallengeResult.Invalid, result);
        Assert.Empty(stepUps.Records);
        Assert.Equal(10, (await _harness.RecoveryCodes.GetUnusedAsync("other-subject", CancellationToken.None)).Count);
        Assert.Equal(
            TotpChallengeResult.Verified,
            await _harness.CreateChallengeService().RedeemRecoveryCodeAsync(TotpTestHarness.User(subjectId: "other-subject"), otherEnrollment.RecoveryCodes[0]));
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_ConcurrentRedemptionOfSameCode_VerifiesExactlyOnce()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readers = 0;
        var store = new ControllableRecoveryCodeStore(_harness.RecoveryCodes)
        {
            // Holds each redemption after its read until both have read, so both see the code as unused.
            AfterRead = async () =>
            {
                if (Interlocked.Increment(ref readers) == 2)
                {
                    bothRead.TrySetResult();
                }

                await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            },
        };
        var stepUps = new RecordingStepUpStore(_harness.StepUps);
        TotpChallengeService service = _harness.CreateChallengeService(stepUps: stepUps, recoveryCodes: store);
        string code = enrollment.RecoveryCodes[0];

        TotpChallengeResult[] results = await Task.WhenAll(
            Task.Run(() => service.RedeemRecoveryCodeAsync(TotpTestHarness.User("session-1"), code).AsTask()),
            Task.Run(() => service.RedeemRecoveryCodeAsync(TotpTestHarness.User("session-2"), code).AsTask()));

        Assert.Equal(2, store.Reads);
        Assert.Single(results, result => result == TotpChallengeResult.Verified);
        Assert.Single(results, result => result == TotpChallengeResult.Invalid);
        Assert.Single(stepUps.Records);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_TryMarkUsedReturnsFalse_IsInvalidWithoutStepUp()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        var store = new ControllableRecoveryCodeStore(_harness.RecoveryCodes) { TryMarkUsedResult = false };
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult result = await _harness.CreateChallengeService(stepUps: stepUps, recoveryCodes: store)
            .RedeemRecoveryCodeAsync(TotpTestHarness.User(), enrollment.RecoveryCodes[0]);

        Assert.Equal(TotpChallengeResult.Invalid, result);
        Assert.Empty(stepUps.Records);
        Assert.DoesNotContain(_harness.ChallengeLogger.Records, record => record.EventId.Id is RecoveryCodeRedeemedEventId or StepUpRecordedEventId);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_OnlyCodesWithMatchingLookupAreHashed()
    {
        var hasher = new CountingHasher();
        string[] codes = ["AAAAA-BBBBB", "AACCC-DDDDD", "ZZZZZ-EEEEE", "QQQQQ-FFFFF", "MMMMM-GGGGG"];
        StoredRecoveryCode[] stored =
        [
            .. codes.Select((code, index) =>
            {
                string normalized = RecoveryCodeGenerator.Normalize(code);
                return new StoredRecoveryCode($"id-{index}", normalized[..2], hasher.Hash(normalized));
            }),
        ];
        _harness.RecoveryCodes.Save(TotpTestHarness.SubjectId, stored);
        string[] matchingHashes = [stored[0].Hash, stored[1].Hash];
        TotpChallengeService service = _harness.CreateChallengeService(hasher: hasher);

        TotpChallengeResult result = await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), "aaccc-ddddd");

        Assert.Equal(TotpChallengeResult.Verified, result);
        Assert.InRange(hasher.VerifiedHashes.Count, 1, 2);
        Assert.All(hasher.VerifiedHashes, hash => Assert.Contains(hash, matchingHashes));
        Assert.Contains(stored[1].Hash, hasher.VerifiedHashes);
    }

    [Theory]
    [InlineData("XYXYX-XYXYX")]
    [InlineData("A")]
    [InlineData("")]
    public async Task RedeemRecoveryCodeAsync_NoCodeWithMatchingLookup_HashesNothing(string code)
    {
        var hasher = new CountingHasher();
        _harness.RecoveryCodes.Save(
            TotpTestHarness.SubjectId,
            [new StoredRecoveryCode("id-0", "AA", hasher.Hash("AAAAABBBBB")), new StoredRecoveryCode("id-1", "ZZ", hasher.Hash("ZZZZZEEEEE"))]);

        TotpChallengeResult result = await _harness.CreateChallengeService(hasher: hasher).RedeemRecoveryCodeAsync(TotpTestHarness.User(), code);

        Assert.Equal(TotpChallengeResult.Invalid, result);
        Assert.Empty(hasher.VerifiedHashes);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_StoredHashNeedsRehash_IsStillVerified()
    {
        TotpEnrollment enrollment = _harness.CreateEnrollmentService(hasher: _harness.Hasher).Create("Contoso", "alice", recoveryCodeCount: 1);
        _harness.RecoveryCodes.Save(TotpTestHarness.SubjectId, enrollment.StoredRecoveryCodes);
        _harness.Hasher.Iterations = 32;

        Assert.Equal(
            HashVerificationResult.SuccessRehashNeeded,
            _harness.Hasher.Verify(enrollment.StoredRecoveryCodes[0].Hash, RecoveryCodeGenerator.Normalize(enrollment.RecoveryCodes[0])));
        Assert.Equal(
            TotpChallengeResult.Verified,
            await _harness.CreateChallengeService().RedeemRecoveryCodeAsync(TotpTestHarness.User(), enrollment.RecoveryCodes[0]));
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_Throttled_ReturnsThrottledWithoutReadingCodes()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        var throttle = new RecordingAttemptThrottle { Throttled = true };
        var store = new ControllableRecoveryCodeStore(_harness.RecoveryCodes);
        var stepUps = new RecordingStepUpStore(_harness.StepUps);

        TotpChallengeResult result = await _harness.CreateChallengeService(throttle, stepUps: stepUps, recoveryCodes: store)
            .RedeemRecoveryCodeAsync(TotpTestHarness.User(), enrollment.RecoveryCodes[0]);

        Assert.Equal(TotpChallengeResult.Throttled, result);
        Assert.Equal(0, store.Reads);
        Assert.Empty(stepUps.Records);
        Assert.Empty(throttle.Attempts);
        Assert.Equal(10, (await _harness.RecoveryCodes.GetUnusedAsync(TotpTestHarness.SubjectId, CancellationToken.None)).Count);
        var record = Assert.Single(_harness.ChallengeLogger.Records);
        Assert.Equal(ChallengeNotAcceptedEventId, record.EventId.Id);
        Assert.True(record.TryGetProperty("Operation", out object? operation));
        Assert.Equal("RecoveryCode", operation);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_Attempts_AreRecordedOnThrottleForSubject()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        var throttle = new RecordingAttemptThrottle();
        TotpChallengeService service = _harness.CreateChallengeService(throttle);

        await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), "AAAAA-AAAAA");
        await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), enrollment.RecoveryCodes[0]);

        Assert.Equal(2, throttle.Attempts.Count);
        Assert.All(throttle.Attempts, key => Assert.Equal(TotpTestHarness.SubjectId, key));
    }

    public static TheoryData<string> CallersWithoutSession => ["ServicePrincipal", "Anonymous", "System", "UserWithoutSession"];

    [Theory]
    [MemberData(nameof(CallersWithoutSession))]
    public async Task RedeemRecoveryCodeAsync_CallerWithoutUserSession_ReturnsNoSessionAndConsumesNothing(string caller)
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        var store = new ControllableRecoveryCodeStore(_harness.RecoveryCodes);
        IUserContext user = caller switch
        {
            "ServicePrincipal" => new FakeUserContext { IdentityKind = IdentityKind.ServicePrincipal, SessionId = "session-1" },
            "Anonymous" => AnonymousUserContext.Instance,
            "System" => SystemUserContext.Instance,
            _ => TotpTestHarness.User(sessionId: null),
        };

        TotpChallengeResult result = await _harness.CreateChallengeService(recoveryCodes: store).RedeemRecoveryCodeAsync(user, enrollment.RecoveryCodes[0]);

        Assert.Equal(TotpChallengeResult.NoSession, result);
        Assert.Equal(0, store.Reads);
        Assert.Equal(10, (await _harness.RecoveryCodes.GetUnusedAsync(TotpTestHarness.SubjectId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_Verified_LogsRemainingCountAndStepUpWithoutCode()
    {
        TotpEnrollment enrollment = _harness.EnrollAndSaveRecoveryCodes();
        string code = enrollment.RecoveryCodes[0];

        await _harness.CreateChallengeService().RedeemRecoveryCodeAsync(TotpTestHarness.User(), code);

        var redeemed = Assert.Single(_harness.ChallengeLogger.Records, record => record.EventId.Id == RecoveryCodeRedeemedEventId);
        Assert.True(redeemed.TryGetProperty("Remaining", out object? remaining));
        Assert.Equal(9, Assert.IsType<int>(remaining));
        var stepUp = Assert.Single(_harness.ChallengeLogger.Records, record => record.EventId.Id == StepUpRecordedEventId);
        Assert.True(stepUp.TryGetProperty("Operation", out object? operation));
        Assert.Equal("RecoveryCode", operation);
        Assert.DoesNotContain(_harness.ChallengeLogger.Records, record => record.EventId.Id == ChallengeNotAcceptedEventId);
        string normalized = RecoveryCodeGenerator.Normalize(code);
        Assert.All(_harness.ChallengeLogger.Records, record =>
        {
            Assert.DoesNotContain(normalized, record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(code, record.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(normalized[..5], record.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_WithRealPbkdf2Hasher_RedeemsLowercaseCode()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SharedKernel:Cryptography:Pbkdf2:Iterations"] = "100000" })
            .Build();
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider();
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();
        TotpEnrollment enrollment = _harness.CreateEnrollmentService(hasher: hasher).Create("Contoso", "alice", recoveryCodeCount: 2);
        _harness.RecoveryCodes.Save(TotpTestHarness.SubjectId, enrollment.StoredRecoveryCodes);
        TotpChallengeService service = _harness.CreateChallengeService(hasher: hasher);

        TotpChallengeResult result = await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), enrollment.RecoveryCodes[1].ToLowerInvariant());

        Assert.Equal(TotpChallengeResult.Verified, result);
        Assert.Equal(TotpChallengeResult.Invalid, await service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), enrollment.RecoveryCodes[1]));
    }

    [Fact]
    public async Task RedeemRecoveryCodeAsync_NullArguments_Throw()
    {
        TotpChallengeService service = _harness.CreateChallengeService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RedeemRecoveryCodeAsync(null!, "AAAAA-AAAAA").AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RedeemRecoveryCodeAsync(TotpTestHarness.User(), null!).AsTask());
    }
}
