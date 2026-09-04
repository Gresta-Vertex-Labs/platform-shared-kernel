using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
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
/// REQUIRES A DOCKER DAEMON. Written to satisfy 18.Idempotency/state-map.md's T-06 through T-10.
/// This package ships no EF Core migrations (README.md's design-time-factory recipe is for real
/// consumers); tests instead create the schema once via <c>EnsureCreatedAsync</c> against the
/// model produced by <see cref="Entities.IdempotencyKeyRecordConfiguration"/>/
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

    private EfCoreIdempotencyKeyStore CreateKeyStore(Guid tenantId, FakeClock clock, EfCoreIdempotencyOptions? options = null) =>
        new(
            CreateContext(_fixture.ConnectionString),
            new FixedTenantAccessor(tenantId),
            clock,
            MsOptions.Create(options ?? new EfCoreIdempotencyOptions()),
            new InMemoryLogger<EfCoreIdempotencyKeyStore>());

    // T-06: N genuinely concurrent HasProcessedAsync calls with the identical (TenantId, Key) —
    // exactly one must observe "not yet processed" (false). Each task gets its own DbContext
    // instance (never shared — DbContext is not thread-safe), all pointed at the same database.
    [Fact]
    public async Task HasProcessedAsync_ConcurrentCallsWithSameKey_ExactlyOneWinsTheReservation()
    {
        var tenantId = Guid.NewGuid();
        var idempotencyKey = $"concurrent-{Guid.NewGuid():N}";
        const int concurrency = 32;
        using var barrier = new Barrier(concurrency);

        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            await using var context = CreateContext(_fixture.ConnectionString);
            var store = new EfCoreIdempotencyKeyStore(
                context,
                new FixedTenantAccessor(tenantId),
                new FakeClock(),
                MsOptions.Create(new EfCoreIdempotencyOptions()),
                new InMemoryLogger<EfCoreIdempotencyKeyStore>());

            barrier.SignalAndWait();
            return await store.HasProcessedAsync(idempotencyKey, CancellationToken.None);
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, hasProcessed => hasProcessed == false);
        Assert.Equal(concurrency - 1, results.Count(hasProcessed => hasProcessed));
    }

    // T-07: a row whose ExpiresAtUtc is already in the past is reclaimed by the next reservation
    // attempt, not treated as a live conflict.
    [Fact]
    public async Task HasProcessedAsync_ExpiredRow_IsReclaimedNotBlocked()
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
                ReservedAtUtc = clock.UtcNow.AddHours(-2),
                ExpiresAtUtc = clock.UtcNow.AddHours(-1), // already expired
                Response = "stale-response-from-a-prior-episode",
            });
            await seedContext.SaveChangesAsync();
        }

        var store = CreateKeyStore(tenantId, clock);

        var hasProcessed = await store.HasProcessedAsync(idempotencyKey, CancellationToken.None);

        Assert.False(hasProcessed); // reclaimed, not blocked
    }

    // T-08: TryGetStoredResponseAsync excludes an expired row's stored response even though the
    // Response column is still physically present (the row has not been deleted by any cleanup job).
    [Fact]
    public async Task TryGetStoredResponseAsync_ForExpiredRow_ReturnsNullDespitePhysicalResponsePresent()
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
                ReservedAtUtc = clock.UtcNow.AddHours(-2),
                ExpiresAtUtc = clock.UtcNow.AddHours(-1),
                Response = "this-response-must-never-resurface",
            });
            await seedContext.SaveChangesAsync();
        }

        var store = CreateKeyStore(tenantId, clock);

        var stored = await store.TryGetStoredResponseAsync(idempotencyKey, CancellationToken.None);

        Assert.Null(stored);
    }

    // T-09: two tenants reserving the identical raw key concurrently must not collide — proves the
    // mandatory TenantId column (not a composite string) actually partitions rows.
    [Fact]
    public async Task HasProcessedAsync_TwoTenantsWithIdenticalRawKey_BothReservationsSucceedIndependently()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedRawKey = $"shared-{Guid.NewGuid():N}";
        var clock = new FakeClock();

        var storeA = CreateKeyStore(tenantA, clock);
        var storeB = CreateKeyStore(tenantB, clock);

        var hasProcessedA = await storeA.HasProcessedAsync(sharedRawKey, CancellationToken.None);
        var hasProcessedB = await storeB.HasProcessedAsync(sharedRawKey, CancellationToken.None);

        Assert.False(hasProcessedA);
        Assert.False(hasProcessedB);
    }

    // T-09-equivalent for the message store.
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

    // T-10 (fail-open/fail-closed against a real Postgres connectivity failure) lives in the
    // standalone EfCoreIdempotencyFailOpenTests class instead of here — it deliberately does NOT
    // require a Docker daemon (an unreachable loopback host:port induces the failure), so it must
    // not be nested inside this Docker-requiring PostgreSqlContainer collection.
}
