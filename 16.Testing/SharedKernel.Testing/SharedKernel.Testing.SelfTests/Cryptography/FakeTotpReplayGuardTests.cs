using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeTotpReplayGuard"/> against <c>01.Core</c>'s <c>ITotpReplayGuard</c>
/// contract — no consuming domain has adopted this fake yet, so this self-test is the only
/// behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeTotpReplayGuardTests
{
    [Fact]
    public async Task HasBeenUsedAsync_UnmarkedCode_ReturnsFalse()
    {
        var guard = new FakeTotpReplayGuard();

        var used = await guard.HasBeenUsedAsync("identity-1", "123456");

        Assert.False(used);
    }

    [Fact]
    public async Task MarkUsedAsync_ThenHasBeenUsedAsync_ReturnsTrue_WithinValidityWindow()
    {
        var guard = new FakeTotpReplayGuard();

        await guard.MarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        var used = await guard.HasBeenUsedAsync("identity-1", "123456");

        Assert.True(used);
    }

    [Fact]
    public async Task HasBeenUsedAsync_AfterClockAdvancesPastWindow_ReturnsFalse_CodeIsFreshAgain()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);

        await guard.MarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        clock.Advance(TimeSpan.FromSeconds(91));
        var used = await guard.HasBeenUsedAsync("identity-1", "123456");

        Assert.False(used);
    }

    [Fact]
    public async Task HasBeenUsedAsync_JustBeforeWindowExpires_StillReturnsTrue()
    {
        var clock = new FakeClock();
        var guard = new FakeTotpReplayGuard(clock);

        await guard.MarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        clock.Advance(TimeSpan.FromSeconds(89));
        var used = await guard.HasBeenUsedAsync("identity-1", "123456");

        Assert.True(used);
    }

    [Fact]
    public async Task HasBeenUsedAsync_DifferentCode_SameIdentity_ReturnsFalse()
    {
        var guard = new FakeTotpReplayGuard();

        await guard.MarkUsedAsync("identity-1", "111111", TimeSpan.FromSeconds(90));
        var used = await guard.HasBeenUsedAsync("identity-1", "222222");

        Assert.False(used);
    }

    [Fact]
    public async Task HasBeenUsedAsync_SameCode_DifferentIdentity_ReturnsFalse()
    {
        var guard = new FakeTotpReplayGuard();

        await guard.MarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));
        var used = await guard.HasBeenUsedAsync("identity-2", "123456");

        Assert.False(used);
    }

    [Fact]
    public async Task Reset_ClearsRecordedUsages()
    {
        var guard = new FakeTotpReplayGuard();
        await guard.MarkUsedAsync("identity-1", "123456", TimeSpan.FromSeconds(90));

        guard.Reset();

        Assert.False(await guard.HasBeenUsedAsync("identity-1", "123456"));
    }

    [Fact]
    public async Task MarkUsedAsync_NullIdentityKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new FakeTotpReplayGuard().MarkUsedAsync(null!, "code", TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task HasBeenUsedAsync_NullCode_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new FakeTotpReplayGuard().HasBeenUsedAsync("identity", null!));
}
