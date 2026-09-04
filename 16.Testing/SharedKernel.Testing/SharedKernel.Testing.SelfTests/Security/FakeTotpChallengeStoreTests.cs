using SharedKernel.Testing.Security;

namespace SharedKernel.Testing.SelfTests.Security;

/// <summary>
/// Proves <see cref="FakeTotpChallengeStore"/> against <c>12.Security.Totp</c>'s
/// <c>ITotpChallengeStore</c> contract — no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeTotpChallengeStoreTests
{
    [Fact]
    public async Task TryGetLastSuccessfulChallengeAsync_NeverRecorded_ReturnsNull()
    {
        var store = new FakeTotpChallengeStore();

        var result = await store.TryGetLastSuccessfulChallengeAsync("identity-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecordSuccessfulChallengeAsync_ThenTryGet_ReturnsRecordedInstant()
    {
        var store = new FakeTotpChallengeStore();
        var verifiedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await store.RecordSuccessfulChallengeAsync("identity-1", verifiedAt);
        var result = await store.TryGetLastSuccessfulChallengeAsync("identity-1");

        Assert.Equal(verifiedAt, result);
    }

    [Fact]
    public async Task RecordSuccessfulChallengeAsync_SecondCall_OverwritesFirst()
    {
        var store = new FakeTotpChallengeStore();
        var first = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2030, 1, 1, 0, 5, 0, TimeSpan.Zero);

        await store.RecordSuccessfulChallengeAsync("identity-1", first);
        await store.RecordSuccessfulChallengeAsync("identity-1", second);
        var result = await store.TryGetLastSuccessfulChallengeAsync("identity-1");

        Assert.Equal(second, result);
    }

    [Fact]
    public async Task RecordSuccessfulChallengeAsync_DifferentIdentities_TrackedIndependently()
    {
        var store = new FakeTotpChallengeStore();
        var instantA = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var instantB = new DateTimeOffset(2030, 2, 1, 0, 0, 0, TimeSpan.Zero);

        await store.RecordSuccessfulChallengeAsync("identity-a", instantA);
        await store.RecordSuccessfulChallengeAsync("identity-b", instantB);

        Assert.Equal(instantA, await store.TryGetLastSuccessfulChallengeAsync("identity-a"));
        Assert.Equal(instantB, await store.TryGetLastSuccessfulChallengeAsync("identity-b"));
    }

    [Fact]
    public async Task Reset_ClearsRecordedChallenges()
    {
        var store = new FakeTotpChallengeStore();
        await store.RecordSuccessfulChallengeAsync("identity-1", DateTimeOffset.UtcNow);

        store.Reset();

        Assert.Null(await store.TryGetLastSuccessfulChallengeAsync("identity-1"));
    }

    [Fact]
    public async Task RecordSuccessfulChallengeAsync_NullIdentityKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new FakeTotpChallengeStore().RecordSuccessfulChallengeAsync(null!, DateTimeOffset.UtcNow));

    [Fact]
    public async Task TryGetLastSuccessfulChallengeAsync_NullIdentityKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new FakeTotpChallengeStore().TryGetLastSuccessfulChallengeAsync(null!));
}
