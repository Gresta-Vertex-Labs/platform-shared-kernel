using System.Globalization;
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
/// <c>ITotpGenerator</c> needs no fake of its own: <c>TotpGenerator</c> is pure and reads time from an injected
/// <c>IClock</c>, so a test composes the REAL generator with the EXISTING <see cref="FakeClock"/> directly. Only
/// <see cref="FakeTotpReplayGuard"/> (no default implementation ships anywhere, by deliberate design) and
/// <see cref="FakeTotpChallengeStore"/> are genuinely load-bearing fakes.
/// </remarks>
public sealed class TotpEnrollmentAndChallengeFlowTests
{
    [Fact]
    public async Task TotpVerifier_ComposedWithFakeClockAndFakeReplayGuard_AcceptsFreshCode_RejectsReplay()
    {
        var clock = new FakeClock();
        var generator = new TotpGenerator(clock);
        var verifier = new TotpVerifier(generator, new FakeTotpReplayGuard(clock));
        byte[] secret = new FakeSecureRandomGenerator().GetBytes(20);

        string code = generator.GenerateCode(secret);

        Assert.Equal(TotpVerificationResult.Valid, await verifier.VerifyAsync("identity-1", secret, code));
        Assert.Equal(TotpVerificationResult.Replayed, await verifier.VerifyAsync("identity-1", secret, code));
    }

    [Fact]
    public async Task TotpVerifier_CodeFromTheNextTimeStep_IsAccepted_ButTheEarlierCodeIsThenRejected()
    {
        var clock = new FakeClock();
        var generator = new TotpGenerator(clock);
        var verifier = new TotpVerifier(generator, new FakeTotpReplayGuard(clock));
        byte[] secret = new FakeSecureRandomGenerator().GetBytes(20);
        string firstCode = generator.GenerateCode(secret);

        clock.Advance(TimeSpan.FromSeconds(TotpParameters.Default.StepSeconds));
        string nextCode = generator.GenerateCode(secret);

        Assert.Equal(TotpVerificationResult.Valid, await verifier.VerifyAsync("identity-1", secret, nextCode));
        Assert.Equal(TotpVerificationResult.Replayed, await verifier.VerifyAsync("identity-1", secret, firstCode));
    }

    [Fact]
    public async Task TotpVerifier_FreshCodeAfterSeveralSteps_IsAcceptedAgain()
    {
        var clock = new FakeClock();
        var generator = new TotpGenerator(clock);
        var verifier = new TotpVerifier(generator, new FakeTotpReplayGuard(clock));
        byte[] secret = new FakeSecureRandomGenerator().GetBytes(20);

        string firstCode = generator.GenerateCode(secret);
        var firstResult = await verifier.VerifyAsync("identity-1", secret, firstCode);

        // Advance well past both the 30s step and the default ±1-step drift window so the second
        // code is genuinely a different time step, not merely still inside the drift tolerance.
        clock.Advance(TimeSpan.FromSeconds(90));
        string secondCode = generator.GenerateCode(secret);
        var secondResult = await verifier.VerifyAsync("identity-1", secret, secondCode);

        Assert.Equal(TotpVerificationResult.Valid, firstResult);
        Assert.Equal(TotpVerificationResult.Valid, secondResult);
    }

    [Fact]
    public async Task TotpVerifier_WrongCode_IsInvalid()
    {
        var clock = new FakeClock();
        var verifier = new TotpVerifier(new TotpGenerator(clock), new FakeTotpReplayGuard(clock));
        byte[] secret = new FakeSecureRandomGenerator(seed: 1).GetBytes(20);
        byte[] otherSecret = new FakeSecureRandomGenerator(seed: 2).GetBytes(20);
        string codeForOtherSecret = new TotpGenerator(clock).GenerateCode(otherSecret);

        var result = await verifier.VerifyAsync("identity-1", secret, codeForOtherSecret);

        Assert.Equal(TotpVerificationResult.Invalid, result);
    }

    [Fact]
    public async Task FullEnrollmentAndChallengeFlow_ComposesEntirelyFromExistingFakes()
    {
        var clock = new FakeClock();
        var random = new FakeSecureRandomGenerator();
        var enrollmentService = new TotpEnrollmentService(random, new RecoveryCodeGenerator(random));
        TotpEnrollment enrollment = enrollmentService.GenerateEnrollment("Contoso", "user@example.com");

        var generator = new TotpGenerator(clock);
        var verifier = new TotpVerifier(generator, new FakeTotpReplayGuard(clock));
        var challengeStore = new FakeTotpChallengeStore();
        var challengeService = new TotpChallengeService(verifier, challengeStore, new InMemoryLogger<TotpChallengeService>());

        var userId = Guid.NewGuid();
        string code = generator.GenerateCode(enrollment.Secret, enrollment.Parameters);

        var verified = await challengeService.VerifyAsync(userId, enrollment.Secret, code, enrollment.Parameters);
        var replayed = await challengeService.VerifyAsync(userId, enrollment.Secret, code, enrollment.Parameters);

        Assert.Equal(TotpVerificationResult.Valid, verified);
        Assert.Equal(TotpVerificationResult.Replayed, replayed);
        Assert.NotNull(await challengeStore.TryGetLastSuccessfulChallengeAsync(userId.ToString("D", CultureInfo.InvariantCulture)));
        Assert.Equal(10, enrollment.RecoveryCodes.Count);
        Assert.Equal("otpauth", enrollment.ProvisioningUri.Scheme);
        Assert.Equal(enrollment.Secret, Base32.Decode(enrollment.SecretBase32).Value);
    }

    [Fact]
    public async Task VerifyAsync_InvalidCode_RecordsNoChallenge()
    {
        var clock = new FakeClock();
        var challengeStore = new FakeTotpChallengeStore();
        var verifier = new TotpVerifier(new TotpGenerator(clock), new FakeTotpReplayGuard(clock));
        var challengeService = new TotpChallengeService(verifier, challengeStore, new InMemoryLogger<TotpChallengeService>());
        var userId = Guid.NewGuid();
        byte[] secret = new FakeSecureRandomGenerator(seed: 3).GetBytes(20);
        string wrongCode = new TotpGenerator(clock).GenerateCode(new FakeSecureRandomGenerator(seed: 4).GetBytes(20));

        var result = await challengeService.VerifyAsync(userId, secret, wrongCode);

        Assert.Equal(TotpVerificationResult.Invalid, result);
        Assert.Null(await challengeStore.TryGetLastSuccessfulChallengeAsync(userId.ToString("D", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task RecordStepUpAsync_RecoveryCodePath_ComposesWithFakeChallengeStore_NoTotpCodeInvolved()
    {
        var challengeStore = new FakeTotpChallengeStore();
        var clock = new FakeClock();
        var verifier = new TotpVerifier(new TotpGenerator(clock), new FakeTotpReplayGuard(clock));
        var challengeService = new TotpChallengeService(verifier, challengeStore, new InMemoryLogger<TotpChallengeService>());
        var userId = Guid.NewGuid();

        // The recovery-code step-up path feeds the same freshness mechanism VerifyAsync does,
        // entirely via the injected FakeTotpChallengeStore, with no live TOTP code involved at all.
        // Guid.Empty is hard-rejected before any store call, per TotpChallengeService's own guard.
        await challengeService.RecordStepUpAsync(userId);
        await challengeService.RecordStepUpAsync(Guid.Empty);

        Assert.NotNull(await challengeStore.TryGetLastSuccessfulChallengeAsync(userId.ToString("D", CultureInfo.InvariantCulture)));
        Assert.Null(await challengeStore.TryGetLastSuccessfulChallengeAsync(Guid.Empty.ToString("D", CultureInfo.InvariantCulture)));
    }
}
