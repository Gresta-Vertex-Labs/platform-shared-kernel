using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.Redis.KeyStore;
using SharedKernel.Idempotency.Redis.MessageStore;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Concurrency;

/// <summary>
/// Concurrency-proving tests against a real Redis container (Testcontainers). Never mock the
/// backing store here — a mock cannot exhibit the race these tests exist to rule out.
/// </summary>
/// <remarks>
/// REQUIRES A DOCKER DAEMON. Fail-open/fail-closed lives in <see cref="RedisIdempotencyFailOpenTests"/>
/// instead — that class deliberately does not require Docker. In an environment with no reachable
/// Docker daemon, these tests fail at collection fixture setup
/// (<see cref="RedisContainerFixture.InitializeAsync"/>), not inside the test bodies themselves.
/// </remarks>
[Collection("RedisContainer")]
public sealed class RedisIdempotencyConcurrencyTests(RedisContainerFixture fixture)
{
    private sealed class FixedTenantAccessor(Guid? tenantId) : ITenantContextAccessor
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private static IConnectionMultiplexer CreateMultiplexer(string connectionString) =>
        ConnectionMultiplexer.Connect(connectionString);

    private static RedisRequestIdempotencyStore CreateStore(
        IConnectionMultiplexer multiplexer,
        Guid? tenantId,
        RedisIdempotencyOptions? options = null) =>
        new(
            multiplexer,
            new FixedTenantAccessor(tenantId),
            MsOptions.Create(options ?? new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(300) }),
            new InMemoryLogger<RedisRequestIdempotencyStore>());

    private static RedisIdempotencyMessageStore CreateMessageStore(
        IConnectionMultiplexer multiplexer,
        Guid? tenantId,
        RedisIdempotencyOptions? redisOptions = null) =>
        new(
            multiplexer,
            new FixedTenantAccessor(tenantId),
            MsOptions.Create(redisOptions ?? new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(300) }),
            MsOptions.Create(new IdempotencyOptions { ExpiryWindow = TimeSpan.FromHours(1) }),
            new InMemoryLogger<RedisIdempotencyMessageStore>());

    // N genuinely concurrent TryBeginAsync calls with the identical key/fingerprint — exactly one
    // must observe Started; the rest must observe InProgress.
    [Fact]
    public async Task TryBeginAsync_ConcurrentCallsWithSameKeyAndFingerprint_ExactlyOneWinsTheReservation()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId);
        var idempotencyKey = $"concurrent-{Guid.NewGuid():N}";
        const string fingerprint = "shared-fingerprint";

        const int concurrency = 32;
        using var barrier = new Barrier(concurrency);

        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Status == IdempotencyBeginStatus.Started);
        Assert.Equal(concurrency - 1, results.Count(r => r.Status == IdempotencyBeginStatus.InProgress));
        Assert.NotNull(results.Single(r => r.Status == IdempotencyBeginStatus.Started).ReservationToken);
    }

    // An unconfirmed reservation self-heals once its short InFlightTtl elapses — the key becomes
    // retryable again with no action from this package.
    [Fact]
    public async Task TryBeginAsync_AfterInFlightTtlElapsesWithoutCompletionOrRelease_BecomesRetryable()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var options = new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(200) };
        var store = CreateStore(multiplexer, tenantId, options);
        var idempotencyKey = $"self-heal-{Guid.NewGuid():N}";

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);

        // Deliberately never call CompleteAsync/ReleaseAsync — simulating a fault.
        await Task.Delay(options.InFlightTtl + TimeSpan.FromMilliseconds(300));

        var second = await store.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, second.Status); // retryable — even a different fingerprint is accepted
    }

    // Two tenants reserving the identical raw key concurrently must not collide.
    [Fact]
    public async Task TryBeginAsync_TwoTenantsWithIdenticalRawKey_BothReservationsSucceedIndependently()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var storeA = CreateStore(multiplexer, tenantA);
        var storeB = CreateStore(multiplexer, tenantB);
        var sharedRawKey = $"shared-{Guid.NewGuid():N}";

        var resultA = await storeA.TryBeginAsync(sharedRawKey, "fingerprint", CancellationToken.None);
        var resultB = await storeB.TryBeginAsync(sharedRawKey, "fingerprint", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.Started, resultA.Status);
        Assert.Equal(IdempotencyBeginStatus.Started, resultB.Status);
    }

    // The message store's own tenant-isolation proof, mirroring the key-store proof above.
    [Fact]
    public async Task MessageStore_TwoTenantsWithIdenticalMessageId_BothReservationsSucceedIndependently()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var storeA = CreateMessageStore(multiplexer, tenantA);
        var storeB = CreateMessageStore(multiplexer, tenantB);
        var sharedMessageId = Guid.NewGuid();

        var reservationA = await storeA.TryBeginAsync(sharedMessageId, CancellationToken.None);
        var reservationB = await storeB.TryBeginAsync(sharedMessageId, CancellationToken.None);

        // Both tenants claim the same message id independently — the keys are tenant-scoped, so
        // neither reservation observes the other.
        Assert.Equal(IdempotencyReservationStatus.Started, reservationA.Status);
        Assert.Equal(IdempotencyReservationStatus.Started, reservationB.Status);
        Assert.NotNull(reservationA.ReservationToken);
        Assert.NotNull(reservationB.ReservationToken);
        Assert.NotEqual(reservationA.ReservationToken, reservationB.ReservationToken);
    }

    // A different fingerprint against an in-flight reservation is reported as FingerprintMismatch,
    // not InProgress.
    [Fact]
    public async Task TryBeginAsync_InProgressReservation_DifferentFingerprint_ReturnsFingerprintMismatch()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId);
        var idempotencyKey = $"mismatch-in-progress-{Guid.NewGuid():N}";

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);

        var second = await store.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.FingerprintMismatch, second.Status);
    }

    // A different fingerprint against a completed key is also FingerprintMismatch — fingerprint
    // comparison always takes priority over status.
    [Fact]
    public async Task TryBeginAsync_CompletedReservation_DifferentFingerprint_ReturnsFingerprintMismatch()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId);
        var idempotencyKey = $"mismatch-completed-{Guid.NewGuid():N}";

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);
        var completed = await store.CompleteAsync(idempotencyKey, first.ReservationToken!, """{"result":"ok"}""", CancellationToken.None);
        Assert.True(completed);

        var second = await store.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.FingerprintMismatch, second.Status);
    }

    // Once completed, the same key/fingerprint replays the exact stored response rather than
    // re-running the guarded work.
    [Fact]
    public async Task TryBeginAsync_CompletedReservation_SameFingerprint_ReturnsStoredResponseVerbatim()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId);
        var idempotencyKey = $"replay-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";
        const string payload = """{"orderId":42,"status":"accepted"}""";

        var first = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);
        await store.CompleteAsync(idempotencyKey, first.ReservationToken!, payload, CancellationToken.None);

        var second = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Completed, second.Status);
        Assert.Equal(payload, second.StoredResponse);
    }

    // ReleaseAsync frees the key immediately, without waiting for InFlightTtl to elapse.
    [Fact]
    public async Task ReleaseAsync_OnInProgressReservation_FreesTheKeyImmediately()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId, new RedisIdempotencyOptions());
        var idempotencyKey = $"release-{Guid.NewGuid():N}";

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);

        var released = await store.ReleaseAsync(idempotencyKey, first.ReservationToken!, CancellationToken.None);
        Assert.True(released);

        var second = await store.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, second.Status);
    }

    // ReleaseAsync must never delete an already-completed entry — "only if not completed" — and must
    // report that outcome back to the caller as false, not throw.
    [Fact]
    public async Task ReleaseAsync_OnCompletedReservation_DoesNotDeleteIt_AndReturnsFalse()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId, new RedisIdempotencyOptions());
        var idempotencyKey = $"release-completed-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";
        const string payload = """{"status":"done"}""";

        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        await store.CompleteAsync(idempotencyKey, begin.ReservationToken!, payload, CancellationToken.None);

        // Deliberately calling ReleaseAsync after Complete, with the very same (still-correct) token
        // — the Lua script's status guard is what must stop the delete, not an unmatched token.
        var released = await store.ReleaseAsync(idempotencyKey, begin.ReservationToken!, CancellationToken.None);
        Assert.False(released);

        var replay = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Completed, replay.Status);
        Assert.Equal(payload, replay.StoredResponse);
    }

    // A stale confirm arriving after a reservation was reclaimed by a different caller must not
    // corrupt the new owner's row — the reservation-token guard's whole reason to exist — and must
    // report that outcome back to the caller as false, not throw.
    [Fact]
    public async Task CompleteAsync_AfterReservationWasReclaimedByAnotherCaller_ReturnsFalse_AndDoesNotCorruptTheNewOwner()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var options = new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(200) };
        var staleOwner = CreateStore(multiplexer, tenantId, options);
        var idempotencyKey = $"stale-owner-{Guid.NewGuid():N}";

        var originalReservation = await staleOwner.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, originalReservation.Status);

        // Let the reservation expire, then have a different store instance (a different caller)
        // reclaim the same key under a different fingerprint.
        await Task.Delay(options.InFlightTtl + TimeSpan.FromMilliseconds(300));
        var newOwner = CreateStore(multiplexer, tenantId, options);
        var newReservation = await newOwner.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, newReservation.Status);

        // The stale owner's late CompleteAsync must report false — it does not hold the current
        // token — and must not touch the row.
        var staleCompleted = await staleOwner.CompleteAsync(
            idempotencyKey, originalReservation.ReservationToken!, """{"from":"stale-owner"}""", CancellationToken.None);
        Assert.False(staleCompleted);

        // The new owner's reservation must still be exactly as it left it: in-flight, its own
        // fingerprint, no response.
        var stillInProgress = await newOwner.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, stillInProgress.Status);
    }

    // A caller-supplied token that never belonged to any reservation on this key must be rejected —
    // false, no exception, and the live reservation must be left completely untouched.
    [Fact]
    public async Task CompleteAsync_WithForeignToken_ReturnsFalse_AndDoesNotCompleteTheRealReservation()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId, new RedisIdempotencyOptions());
        var idempotencyKey = $"foreign-token-complete-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";

        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, begin.Status);

        var foreignToken = Guid.NewGuid().ToString("N");
        Assert.NotEqual(begin.ReservationToken, foreignToken);

        var completed = await store.CompleteAsync(idempotencyKey, foreignToken, """{"from":"foreign"}""", CancellationToken.None);
        Assert.False(completed);

        // The real reservation is unaffected — still in-flight under its own token.
        var stillInProgress = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, stillInProgress.Status);
    }

    // Same guarantee on the release path: a foreign token must not free someone else's reservation.
    [Fact]
    public async Task ReleaseAsync_WithForeignToken_ReturnsFalse_AndDoesNotReleaseTheRealReservation()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenantId, new RedisIdempotencyOptions());
        var idempotencyKey = $"foreign-token-release-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";

        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, begin.Status);

        var foreignToken = Guid.NewGuid().ToString("N");

        var released = await store.ReleaseAsync(idempotencyKey, foreignToken, CancellationToken.None);
        Assert.False(released);

        // The real reservation is unaffected — still in-flight, same fingerprint reports InProgress.
        var stillInProgress = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, stillInProgress.Status);
    }
}
