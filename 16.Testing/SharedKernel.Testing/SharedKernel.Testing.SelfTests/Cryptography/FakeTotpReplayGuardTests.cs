using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeTotpReplayGuard"/> against <c>ITotpReplayGuard</c>'s monotonic time-step contract: per
/// identity, a time step is accepted only when it is later than every step already accepted and still retained.
/// </summary>
/// <remarks>
/// The concurrency tests race real threads on purpose: a sequential test would also pass against a non-atomic
/// check-then-set implementation.
/// </remarks>
public sealed class FakeTotpReplayGuardTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task TryAcceptTimeStepAsync_FirstStepForAnIdentity_IsAccepted()
    {
        var guard = new FakeTotpReplayGuard();

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_SameStepAgain_IsRejected()
    {
        var guard = new FakeTotpReplayGuard();

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_EarlierStepAfterALaterOne_IsRejected()
    {
        var guard = new FakeTotpReplayGuard();

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 101, Retention));
        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_LaterStep_IsAccepted_AndRaisesTheFloor()
    {
        var guard = new FakeTotpReplayGuard();

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 101, Retention));
        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", 101, Retention));
        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_JustBeforeRetentionExpires_StillRejectsTheStep()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);
        await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention);

        clock.Advance(Retention - TimeSpan.FromSeconds(1));

        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_AfterRetentionExpires_ForgetsTheIdentity()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);
        await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention);

        clock.Advance(Retention + TimeSpan.FromSeconds(1));

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_RetentionRunsFromTheLatestAcceptance()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);
        await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention);
        clock.Advance(TimeSpan.FromSeconds(60));
        await guard.TryAcceptTimeStepAsync("identity-1", 102, Retention);

        clock.Advance(TimeSpan.FromSeconds(60));

        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", 101, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_Identities_AreIndependent()
    {
        var guard = new FakeTotpReplayGuard();

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 200, Retention));
        Assert.True(await guard.TryAcceptTimeStepAsync("identity-2", 100, Retention));
        Assert.False(await guard.TryAcceptTimeStepAsync("identity-2", 100, Retention));
        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 201, Retention));
    }

    [Fact]
    public async Task Reset_ForgetsEveryAcceptedStep()
    {
        var guard = new FakeTotpReplayGuard();
        await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention);

        guard.Reset();

        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_NullIdentityKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new FakeTotpReplayGuard().TryAcceptTimeStepAsync(null!, 1, Retention));

    [Fact]
    public async Task TryAcceptTimeStepAsync_ManyConcurrentCallsForTheSameStep_ExactlyOneWins()
    {
        var guard = new FakeTotpReplayGuard();
        const int callers = 50;
        using var barrier = new Barrier(callers);

        bool[] results = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await guard.TryAcceptTimeStepAsync("identity-1", 100, Retention);
        })));

        Assert.Equal(1, results.Count(static accepted => accepted));
    }

    [Fact]
    public async Task TryAcceptTimeStepAsync_ConcurrentCallsForDifferentSteps_LeaveTheHighestStepAsTheFloor()
    {
        var guard = new FakeTotpReplayGuard();
        const int callers = 50;
        using var barrier = new Barrier(callers);

        await Task.WhenAll(Enumerable.Range(1, callers).Select(step => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await guard.TryAcceptTimeStepAsync("identity-1", step, Retention);
        })));

        Assert.False(await guard.TryAcceptTimeStepAsync("identity-1", callers, Retention));
        Assert.True(await guard.TryAcceptTimeStepAsync("identity-1", callers + 1, Retention));
    }
}
