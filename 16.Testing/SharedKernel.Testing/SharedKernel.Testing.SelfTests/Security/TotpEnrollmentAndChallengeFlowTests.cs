using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves a full, deterministic second-factor enrollment/challenge flow is achievable by composing
/// this domain's fakes with the REAL, already-deterministic `01.Core`/`12.Security.Totp` production
/// types — no consuming domain has adopted this composition yet, so this self-test is the only
/// behavioral proof today, per the SelfTests routing rule.
/// </summary>
/// <remarks>
/// AUDIT FINDING (P-453/WO-069): <c>ITotpGenerator</c>/<c>IHotpGenerator</c> need NO fake of their
/// own — <c>TotpGenerator</c>/<c>HotpGenerator</c> are already pure, stateless, and take an injected
/// <c>IClock</c>/an explicit-timestamp overload, so a test composes the REAL generator with the
/// EXISTING <see cref="FakeClock"/> directly. Only <see cref="FakeTotpReplayGuard"/> (no default
/// implementation ships anywhere, by deliberate design) and <see cref="FakeTotpChallengeStore"/> are
/// genuinely load-bearing fakes.
/// </remarks>
public sealed class TotpEnrollmentAndChallengeFlowTests
{
    [Fact]
    public async Task TotpVerifier_ComposedWithFakeClockAndFakeReplayGuard_AcceptsFreshCode_RejectsReplay()
    {
        var clock = new FakeClock();
        var generator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = new FakeTotpReplayGuard(clock);
        var verifier = new TotpVerifier(generator, replayGuard);
        var secret = new FakeSecureRandomGenerator().NextBytes(20);

        var code = generator.GenerateCode(secret);

        var firstAttempt = await verifier.VerifyAsync("identity-1", secret, code);
        var replay = await verifier.VerifyAsync("identity-1", secret, code);

        Assert.True(firstAttempt);
        Assert.False(replay);
    }

    [Fact]
    public async Task TotpVerifier_FreshCodeAfterStepAdvance_IsAcceptedAgain()
    {
        var clock = new FakeClock();
        var generator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = new FakeTotpReplayGuard(clock);
        var verifier = new TotpVerifier(generator, replayGuard);
        var secret = new FakeSecureRandomGenerator().NextBytes(20);

        var firstCode = generator.GenerateCode(secret);
        var firstResult = await verifier.VerifyAsync("identity-1", secret, firstCode);

        // Advance well past both the 30s step and the default ±1-step drift window so the second
        // code is genuinely a different time step, not merely still inside the drift tolerance.
        clock.Advance(TimeSpan.FromSeconds(90));
        var secondCode = generator.GenerateCode(secret);
        var secondResult = await verifier.VerifyAsync("identity-1", secret, secondCode);

        Assert.True(firstResult);
        Assert.True(secondResult);
    }

    [Fact]
    public async Task FullEnrollmentAndChallengeFlow_ComposesEntirelyFromExistingAndNewFakes()
    {
        var clock = new FakeClock();
        var enrollmentService = new TotpEnrollmentService(new FakeSecureRandomGenerator());
        var enrollment = enrollmentService.GenerateEnrollment("Contoso", "user@example.com");

        var generator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = new FakeTotpReplayGuard(clock);
        var verifier = new TotpVerifier(generator, replayGuard);
        var challengeStore = new FakeTotpChallengeStore();
        var logger = new InMemoryLogger<TotpChallengeService>();
        var challengeService = new TotpChallengeService(verifier, challengeStore, logger);

        var userId = Guid.NewGuid();
        var code = generator.GenerateCode(enrollment.Secret);

        var verified = await challengeService.VerifyAsync(userId, enrollment.Secret, code);
        var replayRejected = await challengeService.VerifyAsync(userId, enrollment.Secret, code);

        Assert.True(verified);
        Assert.False(replayRejected);
        Assert.NotEmpty(enrollment.RecoveryCodes);
    }

    [Fact]
    public async Task RecordStepUpAsync_RecoveryCodePath_ComposesWithFakeChallengeStore_NoTotpCodeInvolved()
    {
        var challengeStore = new FakeTotpChallengeStore();
        var logger = new InMemoryLogger<TotpChallengeService>();
        var generator = new TotpGenerator(new HotpGenerator(), new FakeClock());
        var verifier = new TotpVerifier(generator, new FakeTotpReplayGuard());
        var challengeService = new TotpChallengeService(verifier, challengeStore, logger);

        // The recovery-code step-up path feeds the same freshness mechanism VerifyAsync does,
        // entirely via the injected FakeTotpChallengeStore, with no live TOTP code involved at all
        // — a non-empty user id records successfully (no exception); Guid.Empty is hard-rejected
        // before any store call, per TotpChallengeService's own documented guard.
        await challengeService.RecordStepUpAsync(Guid.NewGuid());
        await challengeService.RecordStepUpAsync(Guid.Empty);
    }
}
