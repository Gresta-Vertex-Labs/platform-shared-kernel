using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Challenge;

public sealed class TotpChallengeServiceTests
{
    private static readonly byte[] Secret = "12345678901234567890"u8.ToArray();

    private readonly FakeClock _clock = new();
    private readonly FakeTotpChallengeStore _challengeStore = new();
    private readonly InMemoryLogger<TotpChallengeService> _logger = new();
    private readonly TotpChallengeService _sut;
    private readonly TotpGenerator _totpGenerator;

    public TotpChallengeServiceTests()
    {
        _totpGenerator = new TotpGenerator(new HotpGenerator(), _clock);
        var verifier = new TotpVerifier(_totpGenerator, new FakeTotpReplayGuard());
        _sut = new TotpChallengeService(verifier, _challengeStore, _logger);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAnyArgumentIsNull()
    {
        var verifier = new TotpVerifier(_totpGenerator, new FakeTotpReplayGuard());

        Assert.Throws<ArgumentNullException>(() => new TotpChallengeService(null!, _challengeStore, _logger));
        Assert.Throws<ArgumentNullException>(() => new TotpChallengeService(verifier, null!, _logger));
        Assert.Throws<ArgumentNullException>(() => new TotpChallengeService(verifier, _challengeStore, null!));
    }

    [Fact]
    public async Task VerifyAsync_ValidCodeAgainstRealGeneratedTotp_SucceedsAndRecordsChallenge()
    {
        var userId = Guid.NewGuid();
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var result = await _sut.VerifyAsync(userId, Secret, code);

        Assert.True(result);
        Assert.Equal(1, _challengeStore.RecordCallCount);
    }

    [Fact]
    public async Task VerifyAsync_InvalidCode_FailsAndRecordsNothing()
    {
        var userId = Guid.NewGuid();

        var result = await _sut.VerifyAsync(userId, Secret, "000000");

        Assert.False(result);
        Assert.Equal(0, _challengeStore.RecordCallCount);
        _logger.Records.ShouldHaveLogged(new EventId(12400), LogLevel.Warning);
    }

    [Fact]
    public async Task VerifyAsync_ReplayedCode_FailsOnSecondSubmission()
    {
        var userId = Guid.NewGuid();
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var firstResult = await _sut.VerifyAsync(userId, Secret, code);
        var secondResult = await _sut.VerifyAsync(userId, Secret, code);

        Assert.True(firstResult);
        Assert.False(secondResult);
        Assert.Equal(1, _challengeStore.RecordCallCount);
    }

    [Fact]
    public async Task VerifyAsync_EmptyUserId_RejectedBeforeAnyStoreOrVerifierCall()
    {
        var code = _totpGenerator.GenerateCode(Secret, _clock.UtcNow);

        var result = await _sut.VerifyAsync(Guid.Empty, Secret, code);

        Assert.False(result);
        Assert.Equal(0, _challengeStore.RecordCallCount);
        Assert.Equal(0, _challengeStore.TryGetCallCount);
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
