using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Testing.Idempotency;

namespace SharedKernel.Testing.SelfTests.Idempotency;

public sealed class FakeIdempotencyStoreTests
{
    [Fact]
    public async Task TryBeginAsync_UnknownKey_ReturnsStartedWithReservationToken()
    {
        var store = new FakeIdempotencyStore();

        var result = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.Started, result.Status);
        Assert.Null(result.StoredResponse);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }

    [Fact]
    public async Task TryBeginAsync_SameKeySameFingerprint_NotYetCompleted_ReturnsInProgress()
    {
        var store = new FakeIdempotencyStore();
        await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        var result = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.InProgress, result.Status);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeyDifferentFingerprint_InFlight_ReturnsFingerprintMismatch()
    {
        var store = new FakeIdempotencyStore();
        await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        var result = await store.TryBeginAsync(Req, "key-1", "fp-2", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.FingerprintMismatch, result.Status);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeySameFingerprint_AfterComplete_ReturnsCompletedWithStoredResponse()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        await store.CompleteAsync(Req, "key-1", begin.Token!, "serialized-response", Retention, CancellationToken.None);

        var result = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.Completed, result.Status);
        Assert.Equal("serialized-response", result.StoredResponse);
    }

    [Fact]
    public async Task TryBeginAsync_SameKeyDifferentFingerprint_AfterComplete_ReturnsFingerprintMismatch()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        await store.CompleteAsync(Req, "key-1", begin.Token!, "serialized-response", Retention, CancellationToken.None);

        var result = await store.TryBeginAsync(Req, "key-1", "fp-2", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.FingerprintMismatch, result.Status);
    }

    [Fact]
    public async Task CompleteAsync_TokenMatches_ReturnsTrueAndCompletes()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        var completed = await store.CompleteAsync(Req, "key-1", begin.Token!, "response", Retention, CancellationToken.None);

        Assert.True(completed);
    }

    [Fact]
    public async Task CompleteAsync_NeverReserved_ReturnsFalse()
    {
        var store = new FakeIdempotencyStore();

        var completed = await store.CompleteAsync(Req, "key-1", "some-token", "response", Retention, CancellationToken.None);

        Assert.False(completed);
    }

    [Fact]
    public async Task CompleteAsync_ForeignOrStaleToken_ReturnsFalseAndDoesNotComplete()
    {
        var store = new FakeIdempotencyStore();
        await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        var completed = await store.CompleteAsync(Req, "key-1", "not-the-real-token", "response", Retention, CancellationToken.None);

        Assert.False(completed);

        // The reservation is untouched: a subsequent begin with the same fingerprint still reports InProgress.
        var result = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.InProgress, result.Status);
    }

    [Fact]
    public async Task CompleteAsync_CalledTwiceWithSameToken_SecondCallReturnsFalse()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        await store.CompleteAsync(Req, "key-1", begin.Token!, "response", Retention, CancellationToken.None);

        var secondComplete = await store.CompleteAsync(Req, "key-1", begin.Token!, "different-response", Retention, CancellationToken.None);

        Assert.False(secondComplete);
    }

    [Fact]
    public async Task ReleaseAsync_TokenMatches_ReturnsTrueThenTryBeginAsync_SameKey_ReturnsStartedAgain()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        var released = await store.ReleaseAsync(Req, "key-1", begin.Token!, CancellationToken.None);
        var result = await store.TryBeginAsync(Req, "key-1", "fp-2", Ttl, CancellationToken.None);

        Assert.True(released);
        Assert.Equal(IdempotencyReservationStatus.Started, result.Status);
    }

    [Fact]
    public async Task ReleaseAsync_UnknownKey_ReturnsFalse()
    {
        var store = new FakeIdempotencyStore();

        var released = await store.ReleaseAsync(Req, "never-reserved", "some-token", CancellationToken.None);

        Assert.False(released);
        var result = await store.TryBeginAsync(Req, "never-reserved", "fp-1", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.Started, result.Status);
    }

    [Fact]
    public async Task ReleaseAsync_ForeignOrStaleToken_ReturnsFalseAndDoesNotRelease()
    {
        var store = new FakeIdempotencyStore();
        await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        var released = await store.ReleaseAsync(Req, "key-1", "not-the-real-token", CancellationToken.None);

        Assert.False(released);

        // The reservation is untouched: a subsequent begin with the same fingerprint still reports InProgress.
        var result = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.InProgress, result.Status);
    }

    [Fact]
    public async Task ReleaseAsync_AlreadyCompleted_ReturnsFalse()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        await store.CompleteAsync(Req, "key-1", begin.Token!, "response", Retention, CancellationToken.None);

        var released = await store.ReleaseAsync(Req, "key-1", begin.Token!, CancellationToken.None);

        Assert.False(released);
    }

    [Fact]
    public async Task Calls_RecordsEveryMemberInvocation_InOrder()
    {
        var store = new FakeIdempotencyStore();

        var begin1 = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        await store.CompleteAsync(Req, "key-1", begin1.Token!, "response", Retention, CancellationToken.None);
        var begin2 = await store.TryBeginAsync(Req, "key-2", "fp-2", Ttl, CancellationToken.None);
        await store.ReleaseAsync(Req, "key-2", begin2.Token!, CancellationToken.None);

        Assert.Equal(
            [
                new FakeIdempotencyStore.RecordedCall(nameof(FakeIdempotencyStore.TryBeginAsync), Req, "key-1"),
                new FakeIdempotencyStore.RecordedCall(nameof(FakeIdempotencyStore.CompleteAsync), Req, "key-1"),
                new FakeIdempotencyStore.RecordedCall(nameof(FakeIdempotencyStore.TryBeginAsync), Req, "key-2"),
                new FakeIdempotencyStore.RecordedCall(nameof(FakeIdempotencyStore.ReleaseAsync), Req, "key-2"),
            ],
            store.Calls);
    }

    [Fact]
    public async Task Reset_ClearsReservationsAndRecordedCalls()
    {
        var store = new FakeIdempotencyStore();
        await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        store.Reset();

        Assert.Empty(store.Calls);
        var result = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.Started, result.Status);
    }

    [Fact]
    public async Task TryBeginAsync_NullKey_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeIdempotencyStore().TryBeginAsync(Req, null!, "fp-1", Ttl, CancellationToken.None));

    [Fact]
    public async Task TryBeginAsync_NullFingerprint_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeIdempotencyStore().TryBeginAsync(Req, "key-1", null!, Ttl, CancellationToken.None));

    [Fact]
    public async Task CompleteAsync_NullReservationToken_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeIdempotencyStore().CompleteAsync(Req, "key-1", null!, "response", Retention, CancellationToken.None));

    [Fact]
    public async Task ReleaseAsync_NullReservationToken_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new FakeIdempotencyStore().ReleaseAsync(Req, "key-1", null!, CancellationToken.None));

    [Fact]
    public async Task Purposes_AreSeparateKeySpaces()
    {
        var store = new FakeIdempotencyStore();
        await store.TryBeginAsync(Req, "shared-key", "fp-1", Ttl, CancellationToken.None);

        var message = await store.TryBeginAsync(IdempotencyPurpose.Message, "shared-key", "fp-2", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.Started, message.Status);
    }

    [Fact]
    public async Task Tenants_AreSeparateKeySpaces()
    {
        var store = new FakeIdempotencyStore();
        using (RequestContextScope.Begin(new SystemRequestContext([], tenantId: new SharedKernel.Execution.Tenancy.TenantId(Guid.NewGuid()))))
            await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        using (RequestContextScope.Begin(new SystemRequestContext([], tenantId: new SharedKernel.Execution.Tenancy.TenantId(Guid.NewGuid()))))
        {
            var other = await store.TryBeginAsync(Req, "key-1", "fp-2", Ttl, CancellationToken.None);
            Assert.Equal(IdempotencyReservationStatus.Started, other.Status);
        }
    }

    [Fact]
    public async Task CompleteAsync_WithoutResponse_ReplaysCompletedWithNullResponse()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(IdempotencyPurpose.Message, "m-1", "message", Ttl, CancellationToken.None);
        await store.CompleteAsync(IdempotencyPurpose.Message, "m-1", begin.Token!, null, Retention, CancellationToken.None);

        var again = await store.TryBeginAsync(IdempotencyPurpose.Message, "m-1", "message", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.Completed, again.Status);
        Assert.Null(again.StoredResponse);
        Assert.Equal(Ttl, store.LastTtl);
        Assert.Equal(Retention, store.LastRetention);
    }

    [Fact]
    public async Task Expire_DropsTheReservation_SoTheOldTokenNoLongerOwnsIt()
    {
        var store = new FakeIdempotencyStore();
        var first = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        Assert.True(store.Expire(Req, "key-1"));
        var second = await store.TryBeginAsync(Req, "key-1", "fp-1", Ttl, CancellationToken.None);

        Assert.Equal(IdempotencyReservationStatus.Started, second.Status);
        Assert.False(await store.CompleteAsync(Req, "key-1", first.Token!, "late", Retention, CancellationToken.None));
    }

    private const IdempotencyPurpose Req = IdempotencyPurpose.Request;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);
}
