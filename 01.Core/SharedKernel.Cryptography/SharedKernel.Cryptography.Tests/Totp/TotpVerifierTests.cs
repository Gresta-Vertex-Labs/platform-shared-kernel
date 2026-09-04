using NSubstitute;
using SharedKernel.Cryptography.Tests.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Covers <see cref="TotpVerifier"/> (C-71/T-57) — this phase's headline acceptance criterion: a
/// valid TOTP code cannot be accepted twice inside its validity window.
/// </summary>
public sealed class TotpVerifierTests
{
    private static readonly byte[] Secret = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private const string IdentityKey = "user-42";

    private static (TotpVerifier Verifier, ITotpReplayGuard ReplayGuard, string Code) NewVerifierWithValidCode()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        var totpGenerator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = Substitute.For<ITotpReplayGuard>();

        string code = totpGenerator.GenerateCode(Secret);

        var verifier = new TotpVerifier(totpGenerator, replayGuard);
        return (verifier, replayGuard, code);
    }

    [Fact]
    public async Task VerifyAsync_FreshValidCode_ReturnsTrueAndMarksUsed()
    {
        (TotpVerifier verifier, ITotpReplayGuard replayGuard, string code) = NewVerifierWithValidCode();
        replayGuard.HasBeenUsedAsync(IdentityKey, code, Arg.Any<CancellationToken>()).Returns(false);

        bool result = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.True(result);
        await replayGuard.Received(1).MarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_SameValidCodeSubmittedTwice_SecondCallRejectedByReplayGuard()
    {
        // The phase's headline acceptance criterion: a valid code cannot be accepted twice
        // inside its validity window.
        (TotpVerifier verifier, ITotpReplayGuard replayGuard, string code) = NewVerifierWithValidCode();

        // Simulate the guard's real behavior: unused on the first check, used on the second —
        // exactly what a real store would report once MarkUsedAsync has been called.
        replayGuard.HasBeenUsedAsync(IdentityKey, code, Arg.Any<CancellationToken>()).Returns(false, true);

        bool firstResult = await verifier.VerifyAsync(IdentityKey, Secret, code);
        bool secondResult = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.True(firstResult);
        Assert.False(secondResult);

        // The second call was rejected because HasBeenUsedAsync reported it used — not because
        // the code expired or the clock moved (the clock never advanced in this test) — and it
        // must not have been marked used a second time.
        await replayGuard.Received(2).HasBeenUsedAsync(IdentityKey, code, Arg.Any<CancellationToken>());
        await replayGuard.Received(1).MarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_InvalidCode_ReturnsFalseAndNeverConsultsOrMarksReplayGuard()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        var totpGenerator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = Substitute.For<ITotpReplayGuard>();
        var verifier = new TotpVerifier(totpGenerator, replayGuard);

        bool result = await verifier.VerifyAsync(IdentityKey, Secret, "000000");

        Assert.False(result);

        // A verifier that marks an invalid code as used would let an attacker burn a legitimate
        // code by submitting garbage — this must never happen.
        await replayGuard.DidNotReceive().HasBeenUsedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await replayGuard.DidNotReceive().MarkUsedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_PreviouslyUsedCode_NeverMarkedUsedAgain()
    {
        (TotpVerifier verifier, ITotpReplayGuard replayGuard, string code) = NewVerifierWithValidCode();
        replayGuard.HasBeenUsedAsync(IdentityKey, code, Arg.Any<CancellationToken>()).Returns(true);

        bool result = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.False(result);
        await replayGuard.DidNotReceive().MarkUsedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var totpGenerator = new TotpGenerator(new HotpGenerator(), new SystemClock(timeProvider));
        var replayGuard = Substitute.For<ITotpReplayGuard>();

        Assert.Throws<ArgumentNullException>(() => new TotpVerifier(null!, replayGuard));
        Assert.Throws<ArgumentNullException>(() => new TotpVerifier(totpGenerator, null!));
    }

    [Fact]
    public async Task VerifyAsync_InvalidIdentityKey_Throws()
    {
        (TotpVerifier verifier, _, string code) = NewVerifierWithValidCode();

        await Assert.ThrowsAsync<ArgumentException>(() => verifier.VerifyAsync("", Secret, code).AsTask());
    }
}
