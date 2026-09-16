using NSubstitute;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class TotpVerifierTests
{
    private const string User = "user-42";

    private static readonly byte[] Secret = [.. Enumerable.Range(1, 20).Select(i => (byte)i)];
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private readonly FakeClock _clock = new(Now);
    private readonly TotpGenerator _generator;
    private readonly InMemoryTotpReplayGuard _replayGuard = new();
    private readonly TotpVerifier _verifier;

    public TotpVerifierTests()
    {
        _generator = new TotpGenerator(_clock);
        _verifier = new TotpVerifier(_generator, _replayGuard);
    }

    [Fact]
    public async Task VerifyAsync_ValidCode_ReturnsValid()
    {
        string code = _generator.GenerateCode(Secret);

        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync(User, Secret, code));
    }

    [Fact]
    public async Task VerifyAsync_SameCodeTwice_ReturnsReplayed()
    {
        string code = _generator.GenerateCode(Secret);

        await _verifier.VerifyAsync(User, Secret, code);

        Assert.Equal(TotpVerificationResult.Replayed, await _verifier.VerifyAsync(User, Secret, code));
    }

    [Fact]
    public async Task VerifyAsync_OlderCodeAfterNewerAccepted_ReturnsReplayed()
    {
        string newer = _generator.GenerateCode(Secret, Now.AddSeconds(30));
        string older = _generator.GenerateCode(Secret, Now);

        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync(User, Secret, newer));
        Assert.Equal(TotpVerificationResult.Replayed, await _verifier.VerifyAsync(User, Secret, older));
    }

    [Fact]
    public async Task VerifyAsync_NewerCodeAfterOlderAccepted_ReturnsValid()
    {
        string older = _generator.GenerateCode(Secret, Now.AddSeconds(-30));
        string newer = _generator.GenerateCode(Secret, Now);

        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync(User, Secret, older));
        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync(User, Secret, newer));
    }

    [Fact]
    public async Task VerifyAsync_SameCodeForDifferentIdentities_IsTrackedPerIdentity()
    {
        string code = _generator.GenerateCode(Secret);

        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync("user-1", Secret, code));
        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync("user-2", Secret, code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData("12345")]
    public async Task VerifyAsync_MalformedCode_ReturnsInvalidWithoutConsultingReplayGuard(string code)
    {
        ITotpReplayGuard guard = Substitute.For<ITotpReplayGuard>();
        var verifier = new TotpVerifier(_generator, guard);

        Assert.Equal(TotpVerificationResult.Invalid, await verifier.VerifyAsync(User, Secret, code));
        _ = guard.DidNotReceiveWithAnyArgs().TryAcceptTimeStepAsync(default!, default, default, default);
    }

    [Fact]
    public async Task VerifyAsync_CodeOutsideWindow_ReturnsInvalidWithoutConsultingReplayGuard()
    {
        ITotpReplayGuard guard = Substitute.For<ITotpReplayGuard>();
        var verifier = new TotpVerifier(_generator, guard);
        string stale = _generator.GenerateCode(Secret, Now.AddMinutes(-10));

        Assert.Equal(TotpVerificationResult.Invalid, await verifier.VerifyAsync(User, Secret, stale));
        _ = guard.DidNotReceiveWithAnyArgs().TryAcceptTimeStepAsync(default!, default, default, default);
    }

    [Fact]
    public async Task VerifyAsync_InvalidCodeDoesNotConsumeValidCode()
    {
        string valid = _generator.GenerateCode(Secret);
        string invalid = _generator.GenerateCode(Secret, Now.AddMinutes(-10));

        Assert.Equal(TotpVerificationResult.Invalid, await _verifier.VerifyAsync(User, Secret, invalid));
        Assert.Equal(TotpVerificationResult.Valid, await _verifier.VerifyAsync(User, Secret, valid));
    }

    [Fact]
    public async Task VerifyAsync_PassesMatchedStepRetentionAndToken()
    {
        ITotpReplayGuard guard = Substitute.For<ITotpReplayGuard>();
        guard.TryAcceptTimeStepAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(true));
        var verifier = new TotpVerifier(_generator, guard);
        var parameters = new TotpParameters { StepSeconds = 60, DriftSteps = 2 };
        using var cts = new CancellationTokenSource();
        string code = _generator.GenerateCode(Secret, Now.AddSeconds(-60), parameters);

        TotpVerificationResult result = await verifier.VerifyAsync(User, Secret, code, parameters, cts.Token);

        Assert.Equal(TotpVerificationResult.Valid, result);
        _ = guard.Received(1).TryAcceptTimeStepAsync(User, (Now.ToUnixTimeSeconds() / 60) - 1, TimeSpan.FromSeconds(300), cts.Token);
    }

    [Fact]
    public async Task VerifyAsync_DefaultParameters_RetentionIsDefaultValidityWindow()
    {
        await _verifier.VerifyAsync(User, Secret, _generator.GenerateCode(Secret));

        Assert.Equal(TotpParameters.Default.ValidityWindow, Assert.Single(_replayGuard.Retentions));
        Assert.Equal(TimeSpan.FromSeconds(90), TotpParameters.Default.ValidityWindow);
    }

    [Fact]
    public async Task VerifyAsync_ReplayGuardRejects_ReturnsReplayed()
    {
        ITotpReplayGuard guard = Substitute.For<ITotpReplayGuard>();
        guard.TryAcceptTimeStepAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool>(false));
        var verifier = new TotpVerifier(_generator, guard);

        Assert.Equal(TotpVerificationResult.Replayed, await verifier.VerifyAsync(User, Secret, _generator.GenerateCode(Secret)));
    }

    [Fact]
    public async Task VerifyAsync_ConcurrentSubmissionsOfSameCode_AcceptExactlyOne()
    {
        string code = _generator.GenerateCode(Secret);

        TotpVerificationResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => Task.Run(async () => await _verifier.VerifyAsync(User, Secret, code))));

        Assert.Single(results, r => r == TotpVerificationResult.Valid);
        Assert.Equal(49, results.Count(r => r == TotpVerificationResult.Replayed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VerifyAsync_BlankIdentity_Throws(string identity)
    {
        await Assert.ThrowsAsync<ArgumentException>(async () => await _verifier.VerifyAsync(identity, Secret, "123456"));
    }

    [Fact]
    public async Task VerifyAsync_NullArguments_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _verifier.VerifyAsync(null!, Secret, "123456"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _verifier.VerifyAsync(User, Secret, null!));
        Assert.Throws<ArgumentNullException>(() => new TotpVerifier(null!, _replayGuard));
        Assert.Throws<ArgumentNullException>(() => new TotpVerifier(_generator, null!));
    }
}
