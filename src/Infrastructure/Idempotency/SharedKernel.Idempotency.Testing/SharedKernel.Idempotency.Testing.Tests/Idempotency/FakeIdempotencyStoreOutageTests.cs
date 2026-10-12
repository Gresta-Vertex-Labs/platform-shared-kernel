using SharedKernel.Core.Exceptions;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Idempotency;

namespace SharedKernel.Testing.SelfTests.Idempotency;

public sealed class FakeIdempotencyStoreOutageTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    [Fact]
    public async Task SimulateStoreUnavailable_EveryMember_ThrowsTheClassifiedExceptionAndRecordsTheCall()
    {
        var store = new FakeIdempotencyStore();
        store.SimulateStoreUnavailable();

        var begin = await Assert.ThrowsAsync<IdempotencyStoreUnavailableException>(
            () => store.TryBeginAsync(IdempotencyPurpose.Request, "k", "fp", Ttl, CancellationToken.None));
        var complete = await Assert.ThrowsAsync<IdempotencyStoreUnavailableException>(
            () => store.CompleteAsync(IdempotencyPurpose.Request, "k", "t", "r", Retention, CancellationToken.None));
        var release = await Assert.ThrowsAsync<IdempotencyStoreUnavailableException>(
            () => store.ReleaseAsync(IdempotencyPurpose.Message, "k", "t", CancellationToken.None));

        Assert.True(store.StoreUnavailable);
        foreach (var exception in new[] { begin, complete, release })
        {
            Assert.IsAssignableFrom<SharedKernelException>(exception);
            Assert.Equal(ErrorCodes.Idempotency.StoreUnavailable, exception.Error.Code);
            Assert.Equal(ErrorType.Unavailable, exception.Error.Type);
            Assert.NotNull(exception.InnerException);
        }

        Assert.Equal(
            [
                new FakeIdempotencyStore.RecordedCall(nameof(IIdempotencyStore.TryBeginAsync), IdempotencyPurpose.Request, "k"),
                new FakeIdempotencyStore.RecordedCall(nameof(IIdempotencyStore.CompleteAsync), IdempotencyPurpose.Request, "k"),
                new FakeIdempotencyStore.RecordedCall(nameof(IIdempotencyStore.ReleaseAsync), IdempotencyPurpose.Message, "k"),
            ],
            store.Calls);
    }

    [Fact]
    public async Task RestoreStore_ResumesNormalOperation_AndKeepsEarlierReservations()
    {
        var store = new FakeIdempotencyStore();
        var begin = await store.TryBeginAsync(IdempotencyPurpose.Request, "k", "fp", Ttl, CancellationToken.None);
        store.SimulateStoreUnavailable();
        await Assert.ThrowsAsync<IdempotencyStoreUnavailableException>(
            () => store.TryBeginAsync(IdempotencyPurpose.Request, "k", "fp", Ttl, CancellationToken.None));

        store.RestoreStore();

        Assert.False(store.StoreUnavailable);
        var again = await store.TryBeginAsync(IdempotencyPurpose.Request, "k", "fp", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.InProgress, again.Status);
        Assert.True(await store.CompleteAsync(IdempotencyPurpose.Request, "k", begin.Token!, "r", Retention, CancellationToken.None));
    }

    [Fact]
    public async Task Reset_EndsASimulatedOutage()
    {
        var store = new FakeIdempotencyStore();
        store.SimulateStoreUnavailable();

        store.Reset();

        Assert.False(store.StoreUnavailable);
        var result = await store.TryBeginAsync(IdempotencyPurpose.Request, "k", "fp", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.Started, result.Status);
    }

    [Fact]
    public async Task SimulatedOutage_DoesNotMaskArgumentErrors()
    {
        var store = new FakeIdempotencyStore();
        store.SimulateStoreUnavailable();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.TryBeginAsync((IdempotencyPurpose)9, "k", "fp", Ttl, CancellationToken.None));
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankArguments_ThrowArgumentException_LikeTheRealStores(string blank)
    {
        var store = new FakeIdempotencyStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.TryBeginAsync(IdempotencyPurpose.Request, blank, "fp", Ttl, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.TryBeginAsync(IdempotencyPurpose.Request, "k", blank, Ttl, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CompleteAsync(IdempotencyPurpose.Request, "k", blank, "r", Retention, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CompleteAsync(IdempotencyPurpose.Request, blank, "t", "r", Retention, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReleaseAsync(IdempotencyPurpose.Request, "k", blank, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.ReleaseAsync(IdempotencyPurpose.Request, blank, "t", CancellationToken.None));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task UndefinedPurpose_IsRejectedFirst_ByEveryMember()
    {
        var store = new FakeIdempotencyStore();
        var undefined = (IdempotencyPurpose)5;

        var begin = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.TryBeginAsync(undefined, " ", " ", TimeSpan.Zero, CancellationToken.None));
        var complete = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.CompleteAsync(undefined, " ", " ", null, TimeSpan.Zero, CancellationToken.None));
        var release = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.ReleaseAsync(undefined, " ", " ", CancellationToken.None));

        Assert.All([begin, complete, release], e => Assert.Contains("'5' is not a defined IdempotencyPurpose value.", e.Message));
    }
}
