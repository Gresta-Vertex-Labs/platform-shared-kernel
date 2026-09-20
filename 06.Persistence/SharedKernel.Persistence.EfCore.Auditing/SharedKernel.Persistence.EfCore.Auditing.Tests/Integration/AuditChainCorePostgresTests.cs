using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Auditing.Tests.TestFixtures;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Core <c>EfAuditTrailWriter</c>/<c>EfAuditQueryService</c> proofs against real PostgreSQL: µs
/// timestamp round-trip, contiguous sequencing under real concurrency (with and without the advisory
/// lock), clock-skew immunity, idempotent retry-safety, the page-size cap, and the
/// cross-tenant-privileged read gate.
/// </summary>
[Collection("AuditPostgres")]
public sealed class AuditChainCorePostgresTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public AuditChainCorePostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    private static async Task EnsureCreatedAsync(ServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuditChainTestDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    private static AuditEntry FailedEntry(string resourceType, string resourceId, string? idempotencyKey = null) => new()
    {
        Action = "Tested",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Failed,
        ErrorCode = "test.failure",
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task RecordAsync_MicrosecondTimestamp_RoundTripsExactly_AndVerifiesIntact()
    {
        var connectionString = ConnectionString("sk_audit_us_roundtrip");
        var tenantId = Guid.NewGuid();
        var subMicrosecondClock = new SubMicrosecondFakeClock(
            new DateTimeOffset(2030, 6, 15, 12, 30, 45, TimeSpan.Zero).AddTicks(1234567)); // carries sub-µs ticks

        await using var sp = AuditTestHost.Build(
            connectionString,
            new FakeAuditActorContext(tenantId: tenantId),
            configureServices: services => services.AddSingleton<SharedKernel.Primitives.Clocks.IClock>(subMicrosecondClock));
        await EnsureCreatedAsync(sp);

        AuditRecord written;
        await using (var scope = sp.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            written = await writer.RecordAsync(FailedEntry("Order", "order-1") with { Outcome = AuditOutcome.Succeeded });
        }

        // The truncated value must be exactly what was stored — read a FRESH scope/context (no
        // change-tracker identity-map interference) to prove the round trip through Postgres itself.
        await using (var scope = sp.CreateAsyncScope())
        {
            var queryService = scope.ServiceProvider.GetRequiredService<IAuditQueryService>();
            var result = await queryService.VerifyFullChainAsync(tenantId, "Order");
            result.IsIntact.Should().BeTrue("re-hashing the exact stored microsecond value must reproduce the original digest");
        }

        // The stored value must be truncated to whole microseconds (never carry the sub-µs ticks the
        // clock produced) and must still equal the source value once truncated.
        var expectedTruncatedTicks = subMicrosecondClock.UtcNow.Ticks - (subMicrosecondClock.UtcNow.Ticks % 10);
        written.OccurredOn.Ticks.Should().Be(expectedTruncatedTicks);
    }

    [Fact]
    public async Task RecordAsync_NConcurrentWriters_OnSameChain_WithAdvisoryLock_ProducesContiguousSequence_AndVerifiesIntact()
    {
        const int writerCount = 20;
        var connectionString = ConnectionString("sk_audit_concurrent_locked");
        var tenantId = Guid.NewGuid();

        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId), withAdvisoryLock: true);
        await EnsureCreatedAsync(sp);

        var tasks = Enumerable.Range(0, writerCount).Select(async i =>
        {
            await using var scope = sp.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            return await writer.RecordAsync(FailedEntry("Order", $"order-{i}"));
        });

        var results = await Task.WhenAll(tasks);

        results.Select(r => r.Sequence).OrderBy(s => s).Should().BeEquivalentTo(Enumerable.Range(1, writerCount).Select(i => (long)i));

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Order");
        result.IsIntact.Should().BeTrue();
        result.RecordsChecked.Should().Be(writerCount);
    }

    [Fact]
    public async Task RecordAsync_NConcurrentWriters_WithoutAdvisoryLock_StillProducesContiguousSequence_ViaRetry()
    {
        const int writerCount = 15;
        var connectionString = ConnectionString("sk_audit_concurrent_unlocked");
        var tenantId = Guid.NewGuid();

        // withAdvisoryLock: false — no IAdvisoryTransactionLock registered; the writer must still
        // produce a contiguous, gap-free sequence by falling back to unique-constraint retry alone.
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId), withAdvisoryLock: false);
        await EnsureCreatedAsync(sp);

        var tasks = Enumerable.Range(0, writerCount).Select(async i =>
        {
            await using var scope = sp.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            return await writer.RecordAsync(FailedEntry("Invoice", $"invoice-{i}"));
        });

        var results = await Task.WhenAll(tasks);

        results.Select(r => r.Sequence).OrderBy(s => s).Should().BeEquivalentTo(Enumerable.Range(1, writerCount).Select(i => (long)i));

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Invoice");
        result.IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_ClockJumpsBackwardsBetweenWrites_DoesNotBreakVerification()
    {
        const int writerCount = 10;
        var connectionString = ConnectionString("sk_audit_clock_skew");
        var tenantId = Guid.NewGuid();
        var actorContext = new FakeAuditActorContext(tenantId: tenantId);

        // A clock that jumps BACKWARDS on alternating calls — simulating NTP correction/skew between
        // machines writing to the same chain. Sequence (assigned under the per-chain lock at append
        // time), never OccurredOn, drives chain order and hash-chain linkage, so a non-monotonic
        // timestamp sequence must never produce a false "broken chain" report.
        var skewedClock = new SkewedClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await using var sp = AuditTestHost.Build(
            connectionString, actorContext,
            configureServices: services => services.AddSingleton<SharedKernel.Primitives.Clocks.IClock>(skewedClock));
        await EnsureCreatedAsync(sp);

        var writtenTimestamps = new List<DateTimeOffset>();
        for (var i = 0; i < writerCount; i++)
        {
            await using var scope = sp.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            var record = await writer.RecordAsync(FailedEntry("SkewedOrder", $"order-{i}"));
            writtenTimestamps.Add(record.OccurredOn);
        }

        writtenTimestamps.Should().NotBeInAscendingOrder("the clock deliberately jumps backwards between writes");

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "SkewedOrder");
        result.IsIntact.Should().BeTrue("chain integrity is Sequence-ordered, never OccurredOn-ordered");
        result.RecordsChecked.Should().Be(writerCount);
    }

    [Fact]
    public async Task RecordAsync_SameIdempotencyKeyConcurrently_WritesExactlyOnce()
    {
        var connectionString = ConnectionString("sk_audit_idempotent_concurrent");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = sp.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();
            return await writer.RecordAsync(FailedEntry("Order", "order-1", idempotencyKey: "retry-key-1"));
        });

        var results = await Task.WhenAll(tasks);

        results.Select(r => r.Id).Distinct().Should().ContainSingle("every retry with the same idempotency key must return the SAME record");

        await using var verifyScope = sp.CreateAsyncScope();
        var queryService = verifyScope.ServiceProvider.GetRequiredService<IAuditQueryService>();
        var result = await queryService.VerifyFullChainAsync(tenantId, "Order");
        result.RecordsChecked.Should().Be(1, "eight retries of the same idempotency key must append exactly one record");
    }

    [Fact]
    public async Task RecordAsync_SequentialRetryWithSameIdempotencyKey_ReturnsSameRecord_NoDuplicateOnRetryAfterAcknowledgedCommit()
    {
        var connectionString = ConnectionString("sk_audit_idempotent_sequential");
        var tenantId = Guid.NewGuid();
        await using var sp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        await using var scope1 = sp.CreateAsyncScope();
        var first = await scope1.ServiceProvider.GetRequiredService<IAuditTrailWriter>()
            .RecordAsync(FailedEntry("Order", "order-1", idempotencyKey: "seq-key-1"));

        // A caller retrying RecordAsync after a network blip (the DB write succeeded, but the ack
        // never reached the caller) must get the SAME record back, never a thrown unique-violation.
        await using var scope2 = sp.CreateAsyncScope();
        var second = await scope2.ServiceProvider.GetRequiredService<IAuditTrailWriter>()
            .RecordAsync(FailedEntry("Order", "order-1", idempotencyKey: "seq-key-1"));

        second.Id.Should().Be(first.Id);
        second.Sequence.Should().Be(first.Sequence);
    }

    [Fact]
    public void GetResourceHistoryAsync_PageSizeCap_RejectsAboveMaxPageSize()
    {
        var act = () => new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: AuditQueryLimits.MaxPageSize + 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetResourceHistoryAcrossTenantsAsync_WithoutActiveCrossTenantScope_Throws()
    {
        var connectionString = ConnectionString("sk_audit_crosstenant_reject");
        await using var sp = AuditTestHost.Build(connectionString);
        await EnsureCreatedAsync(sp);

        await using var scope = sp.CreateAsyncScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IAuditQueryService>();

        var act = async () => await queryService.GetResourceHistoryAcrossTenantsAsync(
            "Order", "order-1", afterId: null, descending: false, take: 10);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetResourceHistoryAcrossTenantsAsync_WithActiveCrossTenantScope_SeesEveryTenant()
    {
        var connectionString = ConnectionString("sk_audit_crosstenant_allow");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var spA = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantA));
        await EnsureCreatedAsync(spA);

        AuditRecord recordA;
        await using (var scope = spA.CreateAsyncScope())
        {
            recordA = await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>()
                .RecordAsync(FailedEntry("SharedResource", "shared-1"));
        }

        await using var spB = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: tenantB));
        AuditRecord recordB;
        await using (var scope = spB.CreateAsyncScope())
        {
            recordB = await scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>()
                .RecordAsync(FailedEntry("SharedResource", "shared-1"));
        }

        await using var readerSp = AuditTestHost.Build(connectionString, new FakeAuditActorContext(tenantId: Guid.NewGuid()));
        await using var readerScope = readerSp.CreateAsyncScope();
        // ICrossTenantScope itself exposes only IsActive (read side); entering a scope is the
        // concrete CrossTenantScope's own narrower capability — see its remarks.
        var crossTenantScope = readerScope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.Abstractions.Context.CrossTenantScope>();
        var queryService = readerScope.ServiceProvider.GetRequiredService<IAuditQueryService>();

        using (crossTenantScope.Enter())
        {
            var result = await queryService.GetResourceHistoryAcrossTenantsAsync(
                "SharedResource", "shared-1", afterId: null, descending: false, take: 10);

            result.Items.Select(r => r.Id).Should().BeEquivalentTo([recordA.Id, recordB.Id]);
        }
    }

    /// <summary>A clock that always returns the same, deliberately sub-microsecond-precision instant.</summary>
    private sealed class SubMicrosecondFakeClock(DateTimeOffset fixedInstant) : SharedKernel.Primitives.Clocks.IClock
    {
        public DateTimeOffset UtcNow => fixedInstant;
        public DateOnly Today => DateOnly.FromDateTime(fixedInstant.UtcDateTime);
    }

    /// <summary>A clock that alternates between jumping 5 minutes forward and 8 minutes backward on every read — simulating clock skew/NTP correction between writers.</summary>
    private sealed class SkewedClock(DateTimeOffset start) : SharedKernel.Primitives.Clocks.IClock
    {
        private readonly Lock _gate = new();
        private DateTimeOffset _current = start;
        private bool _forward;

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_gate)
                {
                    _current = _forward ? _current.AddMinutes(5) : _current.AddMinutes(-8);
                    _forward = !_forward;
                    return _current;
                }
            }
        }

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
