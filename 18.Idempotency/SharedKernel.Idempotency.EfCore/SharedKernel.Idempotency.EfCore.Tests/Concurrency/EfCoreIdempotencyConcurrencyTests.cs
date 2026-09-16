using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.KeyStore;
using SharedKernel.Idempotency.EfCore.MessageStore;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Concurrency;

/// <summary>
/// Concurrency-proving tests against a real PostgreSQL container (Testcontainers). Never mock the
/// backing store here — a mock cannot exhibit the race these tests exist to rule out.
/// </summary>
/// <remarks>
/// REQUIRES A DOCKER DAEMON. This package ships no EF Core migrations (README.md's
/// design-time-factory recipe is for real consumers); tests instead create the schema once via
/// <c>EnsureCreatedAsync</c> against the model produced by
/// <see cref="Entities.IdempotencyKeyRecordConfiguration"/>/
/// <see cref="Entities.IdempotencyMessageRecordConfiguration"/>, which is model-equivalent to a
/// real migration for this narrow purpose.
/// </remarks>
[Collection("PostgreSqlContainer")]
public sealed class EfCoreIdempotencyConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EfCoreIdempotencyConcurrencyTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var context = CreateContext(_fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static IdempotencyDbContext CreateContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdempotencyDbContext>();
        optionsBuilder.UsePostgreSQL(connectionString);
        return new IdempotencyDbContext(optionsBuilder.Options);
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantContextAccessor
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private EfCoreRequestIdempotencyStore CreateStore(Guid tenantId, FakeClock clock, EfCoreIdempotencyOptions? options = null) =>
        new(
            CreateContext(_fixture.ConnectionString),
            new FixedTenantAccessor(tenantId),
            clock,
            MsOptions.Create(options ?? new EfCoreIdempotencyOptions()),
            new InMemoryLogger<EfCoreRequestIdempotencyStore>());

    // N genuinely concurrent TryBeginAsync calls with the identical (TenantId, Key, fingerprint) —
    // exactly one must observe Started. Each task gets its own DbContext instance (never shared —
    // DbContext is not thread-safe), all pointed at the same database.
    [Fact]
    public async Task TryBeginAsync_ConcurrentCallsWithSameKeyAndFingerprint_ExactlyOneWinsTheReservation()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"concurrent-{Guid.NewGuid():N}";
        const string fingerprint = "shared-fingerprint";
        const int concurrency = 32;
        using var barrier = new Barrier(concurrency);

        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            await using var context = CreateContext(_fixture.ConnectionString);
            var store = new EfCoreRequestIdempotencyStore(
                context,
                new FixedTenantAccessor(tenantId),
                new FakeClock(),
                MsOptions.Create(new EfCoreIdempotencyOptions()),
                new InMemoryLogger<EfCoreRequestIdempotencyStore>());

            barrier.SignalAndWait();
            return await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Status == IdempotencyBeginStatus.Started);
        Assert.Equal(concurrency - 1, results.Count(r => r.Status == IdempotencyBeginStatus.InProgress));
    }

    // A row whose ExpiresAtUtc is already in the past is reclaimed by the next reservation attempt,
    // not treated as a live conflict — and its stale response never resurfaces.
    [Fact]
    public async Task TryBeginAsync_ExpiredRow_IsReclaimedNotBlocked()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"expired-{Guid.NewGuid():N}";
        var clock = new FakeClock();

        await using (var seedContext = CreateContext(_fixture.ConnectionString))
        {
            seedContext.Add(new Entities.IdempotencyKeyRecord
            {
                TenantId = tenantId,
                Key = idempotencyKey,
                Fingerprint = "stale-fingerprint",
                Status = Entities.IdempotencyRecordStatus.Completed,
                ReservationToken = Guid.NewGuid(),
                ReservedAtUtc = clock.UtcNow.AddHours(-2),
                ExpiresAtUtc = clock.UtcNow.AddHours(-1), // already expired
                Response = "stale-response-from-a-prior-episode",
            });
            await seedContext.SaveChangesAsync();
        }

        var store = CreateStore(tenantId, clock);

        var result = await store.TryBeginAsync(idempotencyKey, "fresh-fingerprint", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.Started, result.Status); // reclaimed, not blocked
    }

    // Two tenants reserving the identical raw key concurrently must not collide — proves the
    // mandatory TenantId column (not a composite string) actually partitions rows.
    [Fact]
    public async Task TryBeginAsync_TwoTenantsWithIdenticalRawKey_BothReservationsSucceedIndependently()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedRawKey = $"shared-{Guid.NewGuid():N}";
        var clock = new FakeClock();

        var storeA = CreateStore(tenantA, clock);
        var storeB = CreateStore(tenantB, clock);

        var resultA = await storeA.TryBeginAsync(sharedRawKey, "fingerprint", CancellationToken.None);
        var resultB = await storeB.TryBeginAsync(sharedRawKey, "fingerprint", CancellationToken.None);

        Assert.Equal(IdempotencyBeginStatus.Started, resultA.Status);
        Assert.Equal(IdempotencyBeginStatus.Started, resultB.Status);
    }

    // The message store's own tenant-isolation proof, mirroring the request-store proof above.
    [Fact]
    public async Task MessageStore_TwoTenantsWithIdenticalMessageId_BothReservationsSucceedIndependently()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedMessageId = Guid.NewGuid();
        var clock = new FakeClock();

        var storeA = new EfCoreIdempotencyMessageStore(
            CreateContext(_fixture.ConnectionString), new FixedTenantAccessor(tenantA), clock,
            MsOptions.Create(new EfCoreIdempotencyOptions()), MsOptions.Create(new IdempotencyOptions()),
            new InMemoryLogger<EfCoreIdempotencyMessageStore>());
        var storeB = new EfCoreIdempotencyMessageStore(
            CreateContext(_fixture.ConnectionString), new FixedTenantAccessor(tenantB), clock,
            MsOptions.Create(new EfCoreIdempotencyOptions()), MsOptions.Create(new IdempotencyOptions()),
            new InMemoryLogger<EfCoreIdempotencyMessageStore>());

        var hasProcessedA = await storeA.HasProcessedAsync(sharedMessageId, CancellationToken.None);
        var hasProcessedB = await storeB.HasProcessedAsync(sharedMessageId, CancellationToken.None);

        Assert.False(hasProcessedA);
        Assert.False(hasProcessedB);
    }

    // A different fingerprint against an in-flight reservation is reported as FingerprintMismatch.
    [Fact]
    public async Task TryBeginAsync_InProgressReservation_DifferentFingerprint_ReturnsFingerprintMismatch()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"mismatch-in-progress-{Guid.NewGuid():N}";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);

        var second = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.FingerprintMismatch, second.Status);
    }

    // A different fingerprint against a completed row is also FingerprintMismatch — fingerprint
    // comparison always takes priority over status.
    [Fact]
    public async Task TryBeginAsync_CompletedReservation_DifferentFingerprint_ReturnsFingerprintMismatch()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"mismatch-completed-{Guid.NewGuid():N}";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);
        var completed = await store.CompleteAsync(idempotencyKey, first.ReservationToken!, """{"result":"ok"}""", CancellationToken.None);
        Assert.True(completed);

        var second = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.FingerprintMismatch, second.Status);
    }

    // Once completed, the same key/fingerprint replays the exact stored response rather than
    // re-running the guarded work — and TryGetStoredResponse-equivalent behavior never resurfaces
    // a stale value once the row is genuinely expired (T-08 equivalent, folded into this test's
    // sibling below).
    [Fact]
    public async Task TryBeginAsync_CompletedReservation_SameFingerprint_ReturnsStoredResponseVerbatim()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"replay-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";
        const string payload = """{"orderId":42,"status":"accepted"}""";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var first = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);
        await store.CompleteAsync(idempotencyKey, first.ReservationToken!, payload, CancellationToken.None);

        var second = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Completed, second.Status);
        Assert.Equal(payload, second.StoredResponse);
    }

    // A row that is expired but whose Response column is still physically present (no cleanup job
    // has run) must never resurface once a fresh TryBeginAsync reclaims it — CompletedAsync's own
    // response reset on reclaim (response = NULL) is what this proves.
    [Fact]
    public async Task TryBeginAsync_ReclaimingExpiredCompletedRow_DoesNotResurfaceStaleResponse()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"expired-response-{Guid.NewGuid():N}";
        var clock = new FakeClock();

        await using (var seedContext = CreateContext(_fixture.ConnectionString))
        {
            seedContext.Add(new Entities.IdempotencyKeyRecord
            {
                TenantId = tenantId,
                Key = idempotencyKey,
                Fingerprint = "stale-fingerprint",
                Status = Entities.IdempotencyRecordStatus.Completed,
                ReservationToken = Guid.NewGuid(),
                ReservedAtUtc = clock.UtcNow.AddHours(-2),
                ExpiresAtUtc = clock.UtcNow.AddHours(-1),
                Response = "this-response-must-never-resurface",
            });
            await seedContext.SaveChangesAsync();
        }

        var store = CreateStore(tenantId, clock);
        var reclaimed = await store.TryBeginAsync(idempotencyKey, "fresh-fingerprint", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, reclaimed.Status);

        // The reclaimed row is now a fresh in-flight reservation under this store's own fingerprint
        // — replaying with that same fingerprint must observe InProgress, never a stale Completed
        // with the old response.
        var replay = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, "fresh-fingerprint", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, replay.Status);
    }

    // ReleaseAsync frees the key immediately, without waiting for InFlightTtl to elapse.
    [Fact]
    public async Task ReleaseAsync_OnInProgressReservation_FreesTheKeyImmediately()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"release-{Guid.NewGuid():N}";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var first = await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, first.Status);

        var released = await store.ReleaseAsync(idempotencyKey, first.ReservationToken!, CancellationToken.None);
        Assert.True(released);

        var second = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, second.Status);
    }

    // ReleaseAsync must never delete an already-completed row — "only if not completed" — and must
    // report that outcome back to the caller as false, not throw.
    [Fact]
    public async Task ReleaseAsync_OnCompletedReservation_DoesNotDeleteIt_AndReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"release-completed-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";
        const string payload = """{"status":"done"}""";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        await store.CompleteAsync(idempotencyKey, begin.ReservationToken!, payload, CancellationToken.None);

        // Deliberately calling ReleaseAsync after Complete on the SAME instance, with the very same
        // (still-correct) token — the Status == InProgress predicate is what must stop the delete,
        // not a mismatched token.
        var released = await store.ReleaseAsync(idempotencyKey, begin.ReservationToken!, CancellationToken.None);
        Assert.False(released);

        var replay = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Completed, replay.Status);
        Assert.Equal(payload, replay.StoredResponse);
    }

    // A stale confirm arriving after a reservation was reclaimed by a different caller must not
    // corrupt the new owner's row — the reservation-token guard's whole reason to exist — and must
    // report that outcome back to the caller as false, not throw.
    [Fact]
    public async Task CompleteAsync_AfterReservationWasReclaimedByAnotherCaller_ReturnsFalse_AndDoesNotCorruptTheNewOwner()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"stale-owner-{Guid.NewGuid():N}";
        var clock = new FakeClock();
        var options = new EfCoreIdempotencyOptions { InFlightTtl = TimeSpan.FromSeconds(5) };

        var staleOwner = CreateStore(tenantId, clock, options);
        var originalReservation = await staleOwner.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, originalReservation.Status);

        // Advance the fake clock past InFlightTtl, then have a different store instance (a
        // different caller) reclaim the same key under a different fingerprint.
        clock.Advance(options.InFlightTtl + TimeSpan.FromSeconds(1));
        var newOwner = CreateStore(tenantId, clock, options);
        var newReservation = await newOwner.TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, newReservation.Status);

        // The stale owner's late CompleteAsync must report false — it does not hold the current
        // token — and must not touch the row.
        var staleCompleted = await staleOwner.CompleteAsync(
            idempotencyKey, originalReservation.ReservationToken!, """{"from":"stale-owner"}""", CancellationToken.None);
        Assert.False(staleCompleted);

        // The new owner's reservation must still be exactly as it left it: in-flight, its own
        // fingerprint, no response.
        var stillInProgress = await CreateStore(tenantId, clock, options).TryBeginAsync(idempotencyKey, "fingerprint-b", CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, stillInProgress.Status);
    }

    // A caller-supplied token that never belonged to any reservation on this key must be rejected —
    // false, no exception, and the live reservation must be left completely untouched.
    [Fact]
    public async Task CompleteAsync_WithForeignToken_ReturnsFalse_AndDoesNotCompleteTheRealReservation()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"foreign-token-complete-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, begin.Status);

        var foreignToken = Guid.NewGuid().ToString();
        Assert.NotEqual(begin.ReservationToken, foreignToken);

        var completed = await store.CompleteAsync(idempotencyKey, foreignToken, """{"from":"foreign"}""", CancellationToken.None);
        Assert.False(completed);

        var stillInProgress = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, stillInProgress.Status);
    }

    // Same guarantee on the release path: a foreign token must not free someone else's reservation.
    [Fact]
    public async Task ReleaseAsync_WithForeignToken_ReturnsFalse_AndDoesNotReleaseTheRealReservation()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"foreign-token-release-{Guid.NewGuid():N}";
        const string fingerprint = "fingerprint-a";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        var begin = await store.TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.Started, begin.Status);

        var foreignToken = Guid.NewGuid().ToString();

        var released = await store.ReleaseAsync(idempotencyKey, foreignToken, CancellationToken.None);
        Assert.False(released);

        var stillInProgress = await CreateStore(tenantId, clock).TryBeginAsync(idempotencyKey, fingerprint, CancellationToken.None);
        Assert.Equal(IdempotencyBeginStatus.InProgress, stillInProgress.Status);
    }

    // A syntactically-invalid (non-Guid) reservation token can never match a real row — CompleteAsync
    // must recognize that without a database round trip and report false, never throw.
    [Fact]
    public async Task CompleteAsync_WithNonGuidToken_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"non-guid-token-{Guid.NewGuid():N}";
        var clock = new FakeClock();
        var store = CreateStore(tenantId, clock);

        await store.TryBeginAsync(idempotencyKey, "fingerprint-a", CancellationToken.None);

        var completed = await store.CompleteAsync(idempotencyKey, "not-a-guid", """{"result":"ok"}""", CancellationToken.None);

        Assert.False(completed);
    }

    // Fail-open/fail-closed against a real Postgres connectivity failure lives in the standalone
    // EfCoreIdempotencyFailOpenTests class instead of here — it deliberately does NOT require a
    // Docker daemon (an unreachable loopback host:port induces the failure), so it must not be
    // nested inside this Docker-requiring PostgreSqlContainer collection.
}
