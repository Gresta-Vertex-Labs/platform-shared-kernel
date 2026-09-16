using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Challenge;

public sealed class TotpChallengeServiceTests
{
    private static readonly byte[] Secret = "12345678901234567890"u8.ToArray();

    private readonly FakeClock _clock = new();
    private readonly FakeTotpChallengeStore _challengeStore = new();
    private readonly InMemoryLogger<TotpChallengeService> _logger = new();
    private readonly TotpGenerator _totpGenerator;
    private readonly TotpVerifier _verifier;
    private readonly TotpChallengeService _sut;

    public TotpChallengeServiceTests()
    {
        _totpGenerator = new TotpGenerator(_clock);
        _verifier = new TotpVerifier(_totpGenerator, new FakeTotpReplayGuard(_clock));
        _sut = new TotpChallengeService(_verifier, _challengeStore, _logger);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAnyArgumentIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TotpChallengeService(null!, _challengeStore, _logger));
        Assert.Throws<ArgumentNullException>(() => new TotpChallengeService(_verifier, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new TotpChallengeService(_verifier, _challengeStore, null!));
    }

    [Fact]
    public async Task VerifyAsync_ValidCodeAgainstRealGeneratedTotp_SucceedsAndRecordsChallenge()
    {
        var userId = Guid.NewGuid();
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var result = await _sut.VerifyAsync(userId, Secret, code);

        Assert.Equal(TotpVerificationResult.Valid, result);
        Assert.Equal(1, _challengeStore.RecordCallCount);
    }

    [Fact]
    public async Task VerifyAsync_InvalidCode_ReturnsInvalidAndRecordsNothing()
    {
        var userId = Guid.NewGuid();
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);
        var wrongCode = code == "000000" ? "111111" : "000000";

        var result = await _sut.VerifyAsync(userId, Secret, wrongCode);

        Assert.Equal(TotpVerificationResult.Invalid, result);
        Assert.Equal(0, _challengeStore.RecordCallCount);
        var record = _logger.Records.ShouldHaveLogged(new EventId(12400), LogLevel.Warning);
        Assert.Contains("InvalidCode", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_ReplayedCode_ReturnsReplayedOnSecondSubmission()
    {
        var userId = Guid.NewGuid();
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var firstResult = await _sut.VerifyAsync(userId, Secret, code);
        var secondResult = await _sut.VerifyAsync(userId, Secret, code);

        Assert.Equal(TotpVerificationResult.Valid, firstResult);
        Assert.Equal(TotpVerificationResult.Replayed, secondResult);
        Assert.Equal(1, _challengeStore.RecordCallCount);
        var record = _logger.Records.ShouldHaveLogged(new EventId(12400), LogLevel.Warning);
        Assert.Contains("ReplayedCode", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(code, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyAsync_OlderCodeAfterNewerStepAccepted_ReturnsReplayed()
    {
        var userId = Guid.NewGuid();
        var olderCode = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);
        _clock.Advance(TimeSpan.FromSeconds(TotpParameters.Default.StepSeconds));
        var newerCode = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var newerResult = await _sut.VerifyAsync(userId, Secret, newerCode);
        var olderResult = await _sut.VerifyAsync(userId, Secret, olderCode);

        Assert.Equal(TotpVerificationResult.Valid, newerResult);
        Assert.Equal(TotpVerificationResult.Replayed, olderResult);
    }

    [Fact]
    public async Task VerifyAsync_SameCodeForDifferentUsers_IsAcceptedForEach()
    {
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var first = await _sut.VerifyAsync(Guid.NewGuid(), Secret, code);
        var second = await _sut.VerifyAsync(Guid.NewGuid(), Secret, code);

        Assert.Equal(TotpVerificationResult.Valid, first);
        Assert.Equal(TotpVerificationResult.Valid, second);
    }

    [Fact]
    public async Task VerifyAsync_NonDefaultParameters_AreUsedForValidation()
    {
        var parameters = new TotpParameters { Digits = 8, StepSeconds = 60, Algorithm = HotpAlgorithm.Sha256 };
        var userId = Guid.NewGuid();
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow, parameters);

        var withDefaults = await _sut.VerifyAsync(userId, Secret, code);
        var withEnrolledParameters = await _sut.VerifyAsync(userId, Secret, code, parameters);

        Assert.Equal(TotpVerificationResult.Invalid, withDefaults);
        Assert.Equal(TotpVerificationResult.Valid, withEnrolledParameters);
    }

    [Fact]
    public async Task VerifyAsync_EmptyUserId_RejectedBeforeAnyStoreOrVerifierCall()
    {
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var result = await _sut.VerifyAsync(Guid.Empty, Secret, code);

        Assert.Equal(TotpVerificationResult.Invalid, result);
        Assert.Equal(0, _challengeStore.RecordCallCount);
        Assert.Equal(0, _challengeStore.TryGetCallCount);

        // The verifier was never called, so the same code still verifies for a real user.
        Assert.Equal(TotpVerificationResult.Valid, await _sut.VerifyAsync(Guid.NewGuid(), Secret, code));
    }

    [Fact]
    public async Task VerifyAsync_NullCode_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.VerifyAsync(Guid.NewGuid(), Secret, null!));
    }

    [Fact]
    public async Task RecordStepUpAsync_EmptyUserId_RejectedBeforeAnyStoreCall()
    {
        await _sut.RecordStepUpAsync(Guid.Empty);

        Assert.Equal(0, _challengeStore.RecordCallCount);
    }

    [Fact]
    public async Task RecordStepUpAsync_ValidUserId_RecordsChallengeWithoutALiveCode()
    {
        var userId = Guid.NewGuid();

        await _sut.RecordStepUpAsync(userId);

        Assert.Equal(1, _challengeStore.RecordCallCount);
    }
}
