using System.Collections.Concurrent;
using System.Threading;
using NSubstitute;
using SharedKernel.Cryptography.Tests.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// A real atomic, lock-free <see cref="ITotpReplayGuard"/> test double backed by
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> — the same atomic-reservation shape a
/// production Redis (<c>SET NX PX</c>) or EF Core (<c>INSERT ... ON CONFLICT DO NOTHING</c>) store
/// uses. Used by <see cref="TotpVerifierTests"/>'s concurrency proof (T-81) instead of an
/// NSubstitute mock, since the whole point of that test is to prove the real check-and-mark
/// operation is indivisible — a mock configured with canned return values could not demonstrate
/// that under genuine concurrent access.
/// </summary>
internal sealed class AtomicInMemoryTotpReplayGuard : ITotpReplayGuard
{
    private readonly ConcurrentDictionary<string, byte> _used = new();

    public ValueTask<bool> TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default) =>
        ValueTask.FromResult(_used.TryAdd($"{identityKey}:{code}", 0));
}

/// <summary>
/// Covers <see cref="TotpVerifier"/> (C-71/T-57, atomic rewrite C-104/T-81/T-82 for P-514/WO-083)
/// — this phase's headline acceptance criterion: two concurrent submissions of the same valid TOTP
/// code resolve to exactly one success and one rejection, and the replay window always matches the
/// step/drift parameters actually used for that verification.
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
        replayGuard.TryMarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        bool result = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.True(result);
        await replayGuard.Received(1).TryMarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_SameValidCodeSubmittedTwiceSequentially_SecondCallRejectedByReplayGuard()
    {
        (TotpVerifier verifier, ITotpReplayGuard replayGuard, string code) = NewVerifierWithValidCode();

        // Simulate the guard's real atomic behavior: the first reservation succeeds, the second
        // (for the identical identityKey/code) fails because it is already reserved.
        replayGuard.TryMarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true, false);

        bool firstResult = await verifier.VerifyAsync(IdentityKey, Secret, code);
        bool secondResult = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.True(firstResult);
        Assert.False(secondResult);

        await replayGuard.Received(2).TryMarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_InvalidCode_ReturnsFalseAndNeverConsultsReplayGuard()
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
        await replayGuard.DidNotReceive().TryMarkUsedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_PreviouslyUsedCode_ReturnsFalse()
    {
        (TotpVerifier verifier, ITotpReplayGuard replayGuard, string code) = NewVerifierWithValidCode();
        replayGuard.TryMarkUsedAsync(IdentityKey, code, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);

        bool result = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.False(result);
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

    // --- T-81: the phase's headline acceptance criterion — a genuine concurrency proof ---

    [Fact]
    public async Task VerifyAsync_TwoConcurrentCallsWithIdenticalValidCode_ExactlyOneSucceedsAndOneIsRejected()
    {
        // This is deliberately NOT a sequential test — a sequential test (call, await, call, await)
        // would pass just as well against the OLD, defective two-step HasBeenUsedAsync+MarkUsedAsync
        // implementation, because sequential calls never actually race. Proving the fix requires
        // both calls to be genuinely in flight at once against a real atomic guard.
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        var totpGenerator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = new AtomicInMemoryTotpReplayGuard();
        var verifier = new TotpVerifier(totpGenerator, replayGuard);

        string code = totpGenerator.GenerateCode(Secret);

        using var barrier = new Barrier(2);

        Task<bool> RaceAsync() =>
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await verifier.VerifyAsync(IdentityKey, Secret, code);
            });

        Task<bool> firstTask = RaceAsync();
        Task<bool> secondTask = RaceAsync();

        bool[] results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, results.Count(static r => r));
        Assert.Equal(1, results.Count(static r => !r));
    }

    [Fact]
    public async Task VerifyAsync_ManyConcurrentCallsWithIdenticalValidCode_ExactlyOneSucceeds()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        var totpGenerator = new TotpGenerator(new HotpGenerator(), clock);
        var replayGuard = new AtomicInMemoryTotpReplayGuard();
        var verifier = new TotpVerifier(totpGenerator, replayGuard);

        string code = totpGenerator.GenerateCode(Secret);
        const int concurrentCallers = 50;

        using var barrier = new Barrier(concurrentCallers);

        IEnumerable<Task<bool>> tasks = Enumerable.Range(0, concurrentCallers).Select(_ =>
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await verifier.VerifyAsync(IdentityKey, Secret, code);
            }));

        bool[] results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(static r => r));
        Assert.Equal(concurrentCallers - 1, results.Count(static r => !r));
    }

    // --- T-82: the replay window must be derived from the actual parameters used, not from a
    // hardcoded default independent of what was validated ---

    [Fact]
    public async Task VerifyAsync_NonDefaultStepAndDrift_ReplayWindowMatchesActualParametersNotHardcodedDefaults()
    {
        const int stepSeconds = 60;
        const int driftWindow = 2;
        // With the removed hardcoded DefaultStepSeconds=30/DefaultDriftWindow=1, the (wrong) old
        // computation would have produced 30 * (2*1+1) = 90 seconds regardless of what was passed
        // here. The correct, config-consistent computation is stepSeconds * (2*driftWindow+1).
        var expectedWindow = TimeSpan.FromSeconds(stepSeconds * ((2 * driftWindow) + 1)); // 60 * 5 = 300s
        Assert.Equal(TimeSpan.FromSeconds(300), expectedWindow);

        var timeProvider = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(3000));
        var clock = new SystemClock(timeProvider);
        var totpGenerator = new TotpGenerator(new HotpGenerator(), clock);
        string code = totpGenerator.GenerateCode(Secret, digits: 6, stepSeconds: stepSeconds);

        var replayGuard = Substitute.For<ITotpReplayGuard>();
        replayGuard.TryMarkUsedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        var verifier = new TotpVerifier(totpGenerator, replayGuard);

        bool result = await verifier.VerifyAsync(
            IdentityKey,
            Secret,
            code,
            digits: 6,
            stepSeconds: stepSeconds,
            driftWindow: driftWindow);

        Assert.True(result);
        await replayGuard.Received(1).TryMarkUsedAsync(IdentityKey, code, expectedWindow, Arg.Any<CancellationToken>());

        // Never the stale default-derived 90-second window.
        await replayGuard.DidNotReceive().TryMarkUsedAsync(IdentityKey, code, TimeSpan.FromSeconds(90), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_DefaultParameters_ReplayWindowIsNinetySeconds()
    {
        // Parity check: at the defaults (stepSeconds=30, driftWindow=1) the window is still the
        // same 90 seconds it always was — this phase changes HOW the window is derived, not its
        // value at the defaults.
        (TotpVerifier verifier, ITotpReplayGuard replayGuard, string code) = NewVerifierWithValidCode();
        replayGuard.TryMarkUsedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        bool result = await verifier.VerifyAsync(IdentityKey, Secret, code);

        Assert.True(result);
        await replayGuard.Received(1).TryMarkUsedAsync(IdentityKey, code, TimeSpan.FromSeconds(90), Arg.Any<CancellationToken>());
    }
}
