using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeTotpReplayGuard"/> against <c>01.Core</c>'s <c>ITotpReplayGuard</c>
/// contract — no consuming domain has adopted this fake yet, so this self-test is the only
/// behavioral proof today, per the SelfTests routing rule.
/// </summary>
/// <remarks>
/// Migrated for P-527/WO-083: the retired two-member <c>HasBeenUsedAsync</c>/<c>MarkUsedAsync</c>
/// shape is gone, replaced by the single atomic <c>TryMarkUsedAsync</c>. Every pre-existing scenario
/// (expiry behaviour, per-identity isolation, <c>Reset()</c>) is preserved, re-expressed against the
/// new single-member API — a fresh/unclaimed code's first <c>TryMarkUsedAsync</c> call returns
/// <see langword="true"/>, a replay returns <see langword="false"/>. Two new tests
/// (<see cref="TryMarkUsedAsync_TwoConcurrentCallsSameCode_ExactlyOneWinner"/>,
/// <see cref="TryMarkUsedAsync_ManyConcurrentCallsSameCode_ExactlyOneWinner"/>) prove the fake's own
/// claim logic is genuinely atomic — this is the reusable proof downstream TOCTOU-regression tests
/// build on, mirroring <c>01.Core</c>'s own <c>TotpVerifierTests</c> pairwise/50-way race pattern.
/// </remarks>
public sealed class FakeTotpReplayGuardTests
{
    [Fact]
    public async Task TryMarkUsedAsync_FreshCode_ReturnsTrue()
    {
        var guard = new FakeTotpReplayGuard();

        var claimed = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));

        Assert.True(claimed);
    }

    [Fact]
    public async Task TryMarkUsedAsync_SecondCallWithinValidityWindow_ReturnsFalse()
    {
        var guard = new FakeTotpReplayGuard();

        var first = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        var replay = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));

        Assert.True(first);
        Assert.False(replay);
    }

    [Fact]
    public async Task TryMarkUsedAsync_AfterClockAdvancesPastWindow_ReturnsTrueAgain_CodeIsFreshAgain()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);

        var first = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        clock.Advance(TimeSpan.FromSeconds(91));
        var afterExpiry = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));

        Assert.True(first);
        Assert.True(afterExpiry);
    }

    [Fact]
    public async Task TryMarkUsedAsync_JustBeforeWindowExpires_StillReturnsFalse()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);

        var first = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        clock.Advance(TimeSpan.FromSeconds(89));
        var stillReplay = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));

        Assert.True(first);
        Assert.False(stillReplay);
    }

    [Fact]
    public async Task TryMarkUsedAsync_DifferentCode_SameIdentity_ReturnsTrue()
    {
        var guard = new FakeTotpReplayGuard();

        var first = await guard.TryMarkUsedAsync("identity-1", "111111", TimeSpan.FromSeconds(90));
        var otherCode = await guard.TryMarkUsedAsync("identity-1", "222222", TimeSpan.FromSeconds(90));

        Assert.True(first);
        Assert.True(otherCode);
    }

    [Fact]
    public async Task TryMarkUsedAsync_SameCode_DifferentIdentity_ReturnsTrue()
    {
        var guard = new FakeTotpReplayGuard();

        var first = await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        var otherIdentity = await guard.TryMarkUsedAsync("identity-2", "123456", TimeSpan.FromSeconds(90));

        Assert.True(first);
        Assert.True(otherIdentity);
    }

    [Fact]
    public async Task Reset_ClearsRecordedUsages()
    {
        var guard = new FakeTotpReplayGuard();
        await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));

        guard.Reset();

        Assert.True(await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public async Task TryMarkUsedAsync_NullIdentityKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new FakeTotpReplayGuard().TryMarkUsedAsync(null!, "code", TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task TryMarkUsedAsync_NullCode_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new FakeTotpReplayGuard().TryMarkUsedAsync("identity", null!, TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task TryMarkUsedAsync_TwoConcurrentCallsSameCode_ExactlyOneWinner()
    {
        // Deliberately NOT a sequential test (call, await, call, await) — a sequential test would
        // pass just as well against a broken, non-atomic two-step check-then-act implementation,
        // because sequential calls never actually race. This is the reusable proof that this fake's
        // claim logic is genuinely atomic, mirroring 01.Core's own TotpVerifierTests race pattern.
        var guard = new FakeTotpReplayGuard();
        using var barrier = new Barrier(2);

        Task<bool> RaceAsync() =>
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
            });

        Task<bool> firstTask = RaceAsync();
        Task<bool> secondTask = RaceAsync();

        bool[] results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, results.Count(static r => r));
        Assert.Equal(1, results.Count(static r => !r));
    }

    [Fact]
    public async Task TryMarkUsedAsync_ManyConcurrentCallsSameCode_ExactlyOneWinner()
    {
        var guard = new FakeTotpReplayGuard();
        const int concurrentCallers = 50;
        using var barrier = new Barrier(concurrentCallers);

        IEnumerable<Task<bool>> tasks = Enumerable.Range(0, concurrentCallers).Select(_ =>
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await guard.TryMarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
            }));

        bool[] results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(static r => r));
        Assert.Equal(concurrentCallers - 1, results.Count(static r => !r));
    }
}
