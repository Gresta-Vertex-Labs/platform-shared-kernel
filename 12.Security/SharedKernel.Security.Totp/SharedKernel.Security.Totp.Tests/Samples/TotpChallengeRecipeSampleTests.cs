using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Security.Totp.Samples;
using SharedKernel.Security.Totp.Tests.Challenge;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Samples;

/// <summary>
/// Exercises the README challenge recipe end to end: enroll, store the hashed recovery codes, then verify a
/// primary code and a recovery code.
/// </summary>
public sealed class TotpChallengeRecipeSampleTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeTotpChallengeStore _challengeStore = new();
    private readonly FakeOneWayHasher _hasher = new();
    private readonly TotpGenerator _generator;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly TotpEnrollment _enrollment;
    private readonly TotpChallengeRecipeSample _sut;

    public TotpChallengeRecipeSampleTests()
    {
        _generator = new TotpGenerator(_clock);
        var random = new SecureRandomGenerator();
        _enrollment = new TotpEnrollmentService(random, new RecoveryCodeGenerator(random))
            .GenerateEnrollment("Contoso", "user@example.com", new TotpParameters { Digits = 8 });

        var stored = new TotpChallengeRecipeSample.SampleEnrollment(
            _enrollment.Secret,
            _enrollment.Parameters,
            [.. _enrollment.RecoveryCodes.Select(code => TotpChallengeRecipeSample.HashRecoveryCode(_hasher, code))]);

        var challengeService = new TotpChallengeService(
            new TotpVerifier(_generator, new FakeTotpReplayGuard(_clock)),
            _challengeStore,
            new InMemoryLogger<TotpChallengeService>());

        _sut = new TotpChallengeRecipeSample(
            new Dictionary<Guid, TotpChallengeRecipeSample.SampleEnrollment> { [_userId] = stored },
            challengeService,
            _hasher);
    }

    [Fact]
    public async Task PrimaryCode_GeneratedWithEnrolledParameters_IsValidOnceThenReplayed()
    {
        var code = _generator.GenerateCode(_enrollment.Secret, _enrollment.Parameters);

        Assert.Equal(TotpVerificationResult.Valid, await _sut.VerifyPrimaryCodeAsync(_userId, code, CancellationToken.None));
        Assert.Equal(TotpVerificationResult.Replayed, await _sut.VerifyPrimaryCodeAsync(_userId, code, CancellationToken.None));
        Assert.Equal(1, _challengeStore.RecordCallCount);
    }

    [Fact]
    public async Task PrimaryCode_UnenrolledUser_IsInvalid()
    {
        var code = _generator.GenerateCode(_enrollment.Secret, _enrollment.Parameters);

        Assert.Equal(TotpVerificationResult.Invalid, await _sut.VerifyPrimaryCodeAsync(Guid.NewGuid(), code, CancellationToken.None));
    }

    [Fact]
    public async Task RecoveryCode_TypedInLowercaseWithoutHyphen_IsAcceptedAndRecordsStepUp()
    {
        var typed = _enrollment.RecoveryCodes[3].Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        Assert.True(await _sut.VerifyRecoveryCodeAsync(_userId, typed, CancellationToken.None));
        Assert.Equal(1, _challengeStore.RecordCallCount);
    }

    [Theory]
    [InlineData("AAAAA-AAAAA")]
    [InlineData("")]
    [InlineData(" - ")]
    public async Task RecoveryCode_Unknown_IsRejected(string typed)
    {
        Assert.False(await _sut.VerifyRecoveryCodeAsync(_userId, typed, CancellationToken.None));
        Assert.Equal(0, _challengeStore.RecordCallCount);
    }
}
