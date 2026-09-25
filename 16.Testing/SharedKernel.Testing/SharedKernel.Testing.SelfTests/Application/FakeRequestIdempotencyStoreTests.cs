using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Testing.Application;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeRequestIdempotencyStoreTests
{
    [Fact]
    public async Task TryBeginAsync_UnknownKey_ReturnsStartedWithReservationToken()
    {
        var store = new FakeRequestIdempotencyStore();

        var result = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
        Assert.Null(result.StoredResponse);
        Assert.False(string.IsNullOrEmpty(result.ReservationToken));
    }

    [Fact]
    public async Task TryBeginAsync_SameKeySameFingerprint_NotYetCompleted_ReturnsInProgress()
    {
        var store = new FakeRequestIdempotencyStore();
        await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        var result = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.InProgress, result.Status);
        Assert.Null(result.ReservationToken);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeyDifferentFingerprint_InFlight_ReturnsFingerprintMismatch()
    {
        var store = new FakeRequestIdempotencyStore();
        await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        var result = await store.TryBeginAsync("key-1", "fp-2", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.FingerprintMismatch, result.Status);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeySameFingerprint_AfterComplete_ReturnsCompletedWithStoredResponse()
    {
        var store = new FakeRequestIdempotencyStore();
        var begin = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("key-1", begin.ReservationToken!, "serialized-response", CancellationToken.None);

        var result = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.Completed, result.Status);
        Assert.Equal("serialized-response", result.StoredResponse);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeyDifferentFingerprint_AfterComplete_ReturnsFingerprintMismatch()
    {
        var store = new FakeRequestIdempotencyStore();
        var begin = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("key-1", begin.ReservationToken!, "serialized-response", CancellationToken.None);

        var result = await store.TryBeginAsync("key-1", "fp-2", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.FingerprintMismatch, result.Status);
    }

    [Fact]
    public async Task CompleteAsync_TokenMatches_ReturnsTrueAndCompletes()
    {
        var store = new FakeRequestIdempotencyStore();
        var begin = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        var completed = await store.CompleteAsync("key-1", begin.ReservationToken!, "response", CancellationToken.None);

        Assert.True(completed);
    }

    [Fact]
    public async Task CompleteAsync_NeverReserved_ReturnsFalse()
    {
        var store = new FakeRequestIdempotencyStore();

        var completed = await store.CompleteAsync("key-1", "some-token", "response", CancellationToken.None);

        Assert.False(completed);
    }

    [Fact]
    public async Task CompleteAsync_ForeignOrStaleToken_ReturnsFalseAndDoesNotComplete()
    {
        var store = new FakeRequestIdempotencyStore();
        await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        var completed = await store.CompleteAsync("key-1", "not-the-real-token", "response", CancellationToken.None);

        Assert.False(completed);

        // The reservation is untouched: a subsequent begin with the same fingerprint still reports InProgress.
        var result = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, result.Status);
    }

    [Fact]
    public async Task CompleteAsync_CalledTwiceWithSameToken_SecondCallReturnsFalse()
    {
        var store = new FakeRequestIdempotencyStore();
        var begin = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("key-1", begin.ReservationToken!, "response", CancellationToken.None);

        var secondComplete = await store.CompleteAsync("key-1", begin.ReservationToken!, "different-response", CancellationToken.None);

        Assert.False(secondComplete);
    }

    [Fact]
    public async Task ReleaseAsync_TokenMatches_ReturnsTrueThenTryBeginAsync_SameKey_ReturnsStartedAgain()
    {
        var store = new FakeRequestIdempotencyStore();
        var begin = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        var released = await store.ReleaseAsync("key-1", begin.ReservationToken!, CancellationToken.None);
        var result = await store.TryBeginAsync("key-1", "fp-2", CancellationToken.None);

        Assert.True(released);
        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
    }

    [Fact]
    public async Task ReleaseAsync_UnknownKey_ReturnsFalse()
    {
        var store = new FakeRequestIdempotencyStore();

        var released = await store.ReleaseAsync("never-reserved", "some-token", CancellationToken.None);

        Assert.False(released);
        var result = await store.TryBeginAsync("never-reserved", "fp-1", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
    }

    [Fact]
    public async Task ReleaseAsync_ForeignOrStaleToken_ReturnsFalseAndDoesNotRelease()
    {
        var store = new FakeRequestIdempotencyStore();
        await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        var released = await store.ReleaseAsync("key-1", "not-the-real-token", CancellationToken.None);

        Assert.False(released);

        // The reservation is untouched: a subsequent begin with the same fingerprint still reports InProgress.
        var result = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, result.Status);
    }

    [Fact]
    public async Task ReleaseAsync_AlreadyCompleted_ReturnsFalse()
    {
        var store = new FakeRequestIdempotencyStore();
        var begin = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("key-1", begin.ReservationToken!, "response", CancellationToken.None);

        var released = await store.ReleaseAsync("key-1", begin.ReservationToken!, CancellationToken.None);

        Assert.False(released);
    }

    [Fact]
    public async Task Calls_RecordsEveryMemberInvocation_InOrder()
    {
        var store = new FakeRequestIdempotencyStore();

        var begin1 = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        await store.CompleteAsync("key-1", begin1.ReservationToken!, "response", CancellationToken.None);
        var begin2 = await store.TryBeginAsync("key-2", "fp-2", CancellationToken.None);
        await store.ReleaseAsync("key-2", begin2.ReservationToken!, CancellationToken.None);

        Assert.Equal(
            [
                new FakeRequestIdempotencyStore.RecordedCall(nameof(FakeRequestIdempotencyStore.TryBeginAsync), "key-1"),
                new FakeRequestIdempotencyStore.RecordedCall(nameof(FakeRequestIdempotencyStore.CompleteAsync), "key-1"),
                new FakeRequestIdempotencyStore.RecordedCall(nameof(FakeRequestIdempotencyStore.TryBeginAsync), "key-2"),
                new FakeRequestIdempotencyStore.RecordedCall(nameof(FakeRequestIdempotencyStore.ReleaseAsync), "key-2"),
            ],
            store.Calls);
    }

    [Fact]
    public async Task Reset_ClearsReservationsAndRecordedCalls()
    {
        var store = new FakeRequestIdempotencyStore();
        await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);

        store.Reset();

        Assert.Empty(store.Calls);
        var result = await store.TryBeginAsync("key-1", "fp-1", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, result.Status);
    }

    [Fact]
    public async Task TryBeginAsync_NullKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeRequestIdempotencyStore().TryBeginAsync(null!, "fp-1", CancellationToken.None));

    [Fact]
    public async Task TryBeginAsync_NullFingerprint_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeRequestIdempotencyStore().TryBeginAsync("key-1", null!, CancellationToken.None));

    [Fact]
    public async Task CompleteAsync_NullReservationToken_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeRequestIdempotencyStore().CompleteAsync("key-1", null!, "response", CancellationToken.None));

    [Fact]
    public async Task ReleaseAsync_NullReservationToken_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeRequestIdempotencyStore().ReleaseAsync("key-1", null!, CancellationToken.None));
}
