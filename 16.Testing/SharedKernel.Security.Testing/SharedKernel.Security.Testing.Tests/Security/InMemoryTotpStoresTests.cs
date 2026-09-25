using SharedKernel.Security.Totp;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

public sealed class InMemoryTotpStepUpStoreTests
{
    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetLastVerifiedAsync_NothingRecorded_ReturnsNull()
    {
        var store = new InMemoryTotpStepUpStore();

        Assert.Null(await store.GetLastVerifiedAsync("user-1", "session-1", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_ThenGet_ReturnsVerifiedAt()
    {
        var store = new InMemoryTotpStepUpStore();

        await store.RecordAsync("user-1", "session-1", VerifiedAt, VerifiedAt.AddMinutes(15), CancellationToken.None);

        Assert.Equal(VerifiedAt, await store.GetLastVerifiedAsync("user-1", "session-1", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_SameSessionAgain_ReplacesEarlierStepUp()
    {
        var store = new InMemoryTotpStepUpStore();
        await store.RecordAsync("user-1", "session-1", VerifiedAt, VerifiedAt.AddMinutes(15), CancellationToken.None);

        await store.RecordAsync("user-1", "session-1", VerifiedAt.AddMinutes(10), VerifiedAt.AddMinutes(25), CancellationToken.None);

        Assert.Equal(VerifiedAt.AddMinutes(10), await store.GetLastVerifiedAsync("user-1", "session-1", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_OneSession_OtherSessionsOfSameUserUnaffected()
    {
        var store = new InMemoryTotpStepUpStore();

        await store.RecordAsync("user-1", "session-1", VerifiedAt, VerifiedAt.AddMinutes(15), CancellationToken.None);

        Assert.Null(await store.GetLastVerifiedAsync("user-1", "session-2", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_OneUser_SameSessionIdOfOtherUserUnaffected()
    {
        var store = new InMemoryTotpStepUpStore();

        await store.RecordAsync("user-1", "session-1", VerifiedAt, VerifiedAt.AddMinutes(15), CancellationToken.None);

        Assert.Null(await store.GetLastVerifiedAsync("user-2", "session-1", CancellationToken.None));
        Assert.Null(await store.GetLastVerifiedAsync("USER-1", "session-1", CancellationToken.None));
    }

    [Fact]
    public async Task Clear_RemovesEveryStepUp()
    {
        var store = new InMemoryTotpStepUpStore();
        await store.RecordAsync("user-1", "session-1", VerifiedAt, VerifiedAt.AddMinutes(15), CancellationToken.None);
        await store.RecordAsync("user-2", "session-9", VerifiedAt, VerifiedAt.AddMinutes(15), CancellationToken.None);

        store.Clear();

        Assert.Null(await store.GetLastVerifiedAsync("user-1", "session-1", CancellationToken.None));
        Assert.Null(await store.GetLastVerifiedAsync("user-2", "session-9", CancellationToken.None));
    }

    [Fact]
    public async Task Methods_NullSubjectOrSession_ThrowArgumentNull()
    {
        var store = new InMemoryTotpStepUpStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.RecordAsync(null!, "s", VerifiedAt, VerifiedAt, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.RecordAsync("u", null!, VerifiedAt, VerifiedAt, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetLastVerifiedAsync(null!, "s", CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetLastVerifiedAsync("u", null!, CancellationToken.None).AsTask());
    }
}

public sealed class InMemoryRecoveryCodeStoreTests
{
    private static readonly DateTimeOffset UsedAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly StoredRecoveryCode CodeA = new("code-a", "ab", "$hash$a");
    private static readonly StoredRecoveryCode CodeB = new("code-b", "cd", "$hash$b");
    private static readonly StoredRecoveryCode CodeC = new("code-c", "ef", "$hash$c");

    [Fact]
    public async Task GetUnusedAsync_UnknownSubject_ReturnsEmpty()
    {
        var store = new InMemoryRecoveryCodeStore();

        Assert.Empty(await store.GetUnusedAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task Save_ThenGetUnused_ReturnsSavedCodes()
    {
        var store = new InMemoryRecoveryCodeStore();

        store.Save("user-1", [CodeA, CodeB]);

        var unused = await store.GetUnusedAsync("user-1", CancellationToken.None);
        Assert.Equal(["code-a", "code-b"], unused.Select(code => code.Id).Order(StringComparer.Ordinal));
        Assert.Contains(CodeA, unused);
    }

    [Fact]
    public async Task Save_Again_ReplacesEarlierCodes()
    {
        var store = new InMemoryRecoveryCodeStore();
        store.Save("user-1", [CodeA, CodeB]);

        store.Save("user-1", [CodeC]);

        Assert.Equal([CodeC], await store.GetUnusedAsync("user-1", CancellationToken.None));
        Assert.False(await store.TryMarkUsedAsync("user-1", "code-a", UsedAt, CancellationToken.None));
    }

    [Fact]
    public async Task Save_Again_RestoresPreviouslyUsedCodeIds()
    {
        var store = new InMemoryRecoveryCodeStore();
        store.Save("user-1", [CodeA]);
        await store.TryMarkUsedAsync("user-1", "code-a", UsedAt, CancellationToken.None);

        store.Save("user-1", [CodeA]);

        Assert.Equal([CodeA], await store.GetUnusedAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task TryMarkUsedAsync_UnusedCode_ReturnsTrueOnceAndRemovesIt()
    {
        var store = new InMemoryRecoveryCodeStore();
        store.Save("user-1", [CodeA, CodeB]);

        Assert.True(await store.TryMarkUsedAsync("user-1", "code-a", UsedAt, CancellationToken.None));
        Assert.False(await store.TryMarkUsedAsync("user-1", "code-a", UsedAt, CancellationToken.None));
        Assert.Equal([CodeB], await store.GetUnusedAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task TryMarkUsedAsync_ConcurrentSameCode_ExactlyOneSucceeds()
    {
        var store = new InMemoryRecoveryCodeStore();
        store.Save("user-1", [CodeA]);

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => store.TryMarkUsedAsync("user-1", "code-a", UsedAt, CancellationToken.None).AsTask())));

        Assert.Single(results, marked => marked);
    }

    [Fact]
    public async Task TryMarkUsedAsync_UnknownSubject_ReturnsFalse()
    {
        var store = new InMemoryRecoveryCodeStore();

        Assert.False(await store.TryMarkUsedAsync("user-1", "code-a", UsedAt, CancellationToken.None));
    }

    [Fact]
    public async Task TryMarkUsedAsync_UnknownCode_ReturnsFalse()
    {
        var store = new InMemoryRecoveryCodeStore();
        store.Save("user-1", [CodeA]);

        Assert.False(await store.TryMarkUsedAsync("user-1", "code-x", UsedAt, CancellationToken.None));
        Assert.False(await store.TryMarkUsedAsync("user-1", "CODE-A", UsedAt, CancellationToken.None));
        Assert.Equal([CodeA], await store.GetUnusedAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task TryMarkUsedAsync_OtherUsersCode_ReturnsFalseAndLeavesItUnused()
    {
        var store = new InMemoryRecoveryCodeStore();
        store.Save("user-1", [CodeA]);
        store.Save("user-2", [CodeB]);

        Assert.False(await store.TryMarkUsedAsync("user-2", "code-a", UsedAt, CancellationToken.None));
        Assert.Equal([CodeA], await store.GetUnusedAsync("user-1", CancellationToken.None));
    }

    [Fact]
    public async Task Methods_NullArguments_ThrowArgumentNull()
    {
        var store = new InMemoryRecoveryCodeStore();

        Assert.Throws<ArgumentNullException>(() => store.Save(null!, []));
        Assert.Throws<ArgumentNullException>(() => store.Save("user-1", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetUnusedAsync(null!, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.TryMarkUsedAsync(null!, "c", UsedAt, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.TryMarkUsedAsync("u", null!, UsedAt, CancellationToken.None).AsTask());
    }
}
