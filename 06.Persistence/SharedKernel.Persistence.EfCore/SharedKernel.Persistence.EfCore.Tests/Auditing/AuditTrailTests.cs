using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Auditing;

// ---------------------------------------------------------------------------
// C-153..C-165 test DbContext
// ---------------------------------------------------------------------------

/// <summary>
/// Test DbContext scoped to <see cref="AuditRecord"/> only, applying the REAL
/// <see cref="AuditRecordEntityConfiguration"/> directly (never <c>base.OnModelCreating</c>, which
/// would sweep in every other test file's unrelated entity configurations from this shared test
/// assembly — the same reason every other test DbContext in this project hand-rolls
/// <c>OnModelCreating</c>).
/// </summary>
internal sealed class AuditTestDbContext : SharedKernelDbContext
{
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    public AuditTestDbContext(
        DbContextOptions<AuditTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IEnumerable<ISaveChangesInterceptor> additionalInterceptors)
        : base(options, audit, softDelete, concurrency, additionalInterceptors)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditRecordEntityConfiguration());
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

/// <summary>
/// WO-071/P-457 — <see cref="EfAuditTrailWriter"/>, <see cref="EfAuditQueryService"/>,
/// <see cref="AuditRecordImmutabilityInterceptor"/>, and <see cref="EfCoreAuditActorContext"/>.
/// </summary>
public sealed class AuditTrailTests
{
    // WO-071/P-457: the InMemory provider (not SQLite) is deliberate here — see
    // Directory.Packages.props' remarks on Microsoft.EntityFrameworkCore.InMemory for why SQLite
    // cannot execute AuditRecord's OccurredOn-ordered (DateTimeOffset) queries at all.
    private static AuditTestDbContext CreateContext(string? databaseName = null, bool withImmutabilityGuard = true)
    {
        var options = new DbContextOptionsBuilder<AuditTestDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var interceptors = withImmutabilityGuard
            ? new ISaveChangesInterceptor[] { new AuditRecordImmutabilityInterceptor() }
            : [];

        var ctx = new AuditTestDbContext(
            options,
            new AuditInterceptor(userCtx, clock, svcOpts),
            new SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new ConcurrencyInterceptor(),
            interceptors);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IAuditActorContext CreateActorContext(Guid tenantId, string actorId)
    {
        var actor = Substitute.For<IAuditActorContext>();
        actor.TenantId.Returns(tenantId);
        actor.ActorId.Returns(actorId);
        return actor;
    }

    // A monotonically-increasing fake clock. Deliberately NOT a single frozen timestamp: two
    // AuditRecords written within the same millisecond would then rely entirely on
    // Guid.CreateVersion7()'s sub-millisecond bits to break the (OccurredOn, Id) sort tie, and
    // those bits are cryptographically random — NOT guaranteed to preserve generation order
    // (confirmed empirically: ~50% of same-millisecond Guid.CreateVersion7() pairs sort opposite
    // to their generation order, under both Guid.CompareTo and byte-lexicographic comparison).
    // Real IClock.UtcNow in production reflects genuine wall-clock time, so this scenario is rare
    // there; a test that fires several writes back-to-back needs a clock that does not tie.
    private static IClock CreateIncrementingClock(DateTimeOffset start)
    {
        var current = start;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => current = current.AddMilliseconds(1));
        return clock;
    }

    private static EfAuditTrailWriter CreateWriter(
        AuditTestDbContext context, IAuditActorContext actorContext, IClock? clock = null) =>
        new(context, clock ?? CreateIncrementingClock(DateTimeOffset.UtcNow),
            new Sha256ContentHasher(), actorContext);

    private static EfAuditQueryService CreateQueryService(AuditTestDbContext context) =>
        new(context, new SpecificationEvaluator<AuditRecord>(), new Sha256ContentHasher());

    // -----------------------------------------------------------------------
    // C-161/C-153/C-154 — EfAuditTrailWriter
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RecordAsync_FirstRecordInPartition_HasNullPreviousHash()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var writer = CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"));

        var record = await writer.RecordAsync(new AuditEntry
        {
            Action = "OrderCreated",
            ResourceType = "Order",
            ResourceId = "order-1",
        });

        record.PreviousRecordHash.Should().BeNull();
        record.RecordHash.Should().NotBeNullOrWhiteSpace();
        record.TenantId.Should().Be(tenantId);
        record.ActorId.Should().Be("actor-1");
        record.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RecordAsync_SecondRecordInSamePartition_ChainsToFirst()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var writer = CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"));

        var first = await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
        var second = await writer.RecordAsync(new AuditEntry { Action = "Approved", ResourceType = "Order", ResourceId = "order-1" });

        second.PreviousRecordHash.Should().Be(first.RecordHash);
    }

    [Fact]
    public async Task RecordAsync_DifferentResourceTypePartition_DoesNotChainAcrossPartitions()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var writer = CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"));

        await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
        var otherPartition = await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Invoice", ResourceId = "invoice-1" });

        otherPartition.PreviousRecordHash.Should().BeNull("Invoice is a different (TenantId, ResourceType) partition from Order");
    }

    [Fact]
    public async Task RecordAsync_DifferentTenantPartition_DoesNotChainAcrossTenants()
    {
        using var ctx = CreateContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await CreateWriter(ctx, CreateActorContext(tenantA, "actor-1"))
            .RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });

        var tenantBRecord = await CreateWriter(ctx, CreateActorContext(tenantB, "actor-1"))
            .RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });

        tenantBRecord.PreviousRecordHash.Should().BeNull("tenant B's chain is independent of tenant A's");
    }

    [Fact]
    public async Task RecordAsync_PreservesBeforeAfterSnapshotsAndCorrelationApprovalIds()
    {
        using var ctx = CreateContext();
        var writer = CreateWriter(ctx, CreateActorContext(Guid.NewGuid(), "actor-1"));

        var record = await writer.RecordAsync(new AuditEntry
        {
            Action = "LimitChanged",
            ResourceType = "Account",
            ResourceId = "acct-1",
            BeforeSnapshot = "{\"limit\":100}",
            AfterSnapshot = "{\"limit\":200}",
            CorrelationId = "corr-123",
            ApprovalId = "appr-456",
        });

        record.BeforeSnapshot.Should().Be("{\"limit\":100}");
        record.AfterSnapshot.Should().Be("{\"limit\":200}");
        record.CorrelationId.Should().Be("corr-123");
        record.ApprovalId.Should().Be("appr-456");
    }

    [Fact]
    public async Task RecordAsync_UsesClockForOccurredOn_NeverCallerSupplied()
    {
        using var ctx = CreateContext();
        var fixedNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var writer = CreateWriter(ctx, CreateActorContext(Guid.NewGuid(), "actor-1"), TestDbContextFactory.CreateClock(fixedNow));

        var record = await writer.RecordAsync(new AuditEntry { Action = "X", ResourceType = "Y", ResourceId = "z" });

        record.OccurredOn.Should().Be(fixedNow);
    }

    // -----------------------------------------------------------------------
    // C-162 — AuditRecordImmutabilityInterceptor / AuditRecordImmutableException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Interceptor_ThrowsAuditRecordImmutableException_OnUpdate()
    {
        using var ctx = CreateContext();
        var writer = CreateWriter(ctx, CreateActorContext(Guid.NewGuid(), "actor-1"));
        var record = await writer.RecordAsync(new AuditEntry { Action = "X", ResourceType = "Y", ResourceId = "z" });
        ctx.ChangeTracker.Clear();

        var tracked = await ctx.AuditRecords.FindAsync(record.Id);
        ctx.Entry(tracked!).State = EntityState.Modified;

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<AuditRecordImmutableException>())
            .Which.RecordId.Should().Be(record.Id);
    }

    [Fact]
    public async Task Interceptor_ThrowsAuditRecordImmutableException_OnDelete()
    {
        using var ctx = CreateContext();
        var writer = CreateWriter(ctx, CreateActorContext(Guid.NewGuid(), "actor-1"));
        var record = await writer.RecordAsync(new AuditEntry { Action = "X", ResourceType = "Y", ResourceId = "z" });
        ctx.ChangeTracker.Clear();

        var tracked = await ctx.AuditRecords.FindAsync(record.Id);
        ctx.AuditRecords.Remove(tracked!);

        var act = async () => await ctx.SaveChangesAsync();

        (await act.Should().ThrowAsync<AuditRecordImmutableException>())
            .Which.RecordId.Should().Be(record.Id);
    }

    [Fact]
    public async Task Interceptor_DoesNotInterfereWithAdding_NewAuditRecords()
    {
        // A freshly-Added AuditRecord must never trip the immutability guard — proven implicitly
        // by every RecordAsync test above succeeding, and explicitly here across multiple writes.
        using var ctx = CreateContext();
        var writer = CreateWriter(ctx, CreateActorContext(Guid.NewGuid(), "actor-1"));

        var act = async () =>
        {
            await writer.RecordAsync(new AuditEntry { Action = "A", ResourceType = "T", ResourceId = "1" });
            await writer.RecordAsync(new AuditEntry { Action = "B", ResourceType = "T", ResourceId = "1" });
        };

        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // C-163 — EfAuditQueryService
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetResourceHistoryAsync_ReturnsOnlyMatchingResource_OrderedAsRequested()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var writer = CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"));

        await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
        await writer.RecordAsync(new AuditEntry { Action = "Approved", ResourceType = "Order", ResourceId = "order-1" });
        await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-2" });

        var queryService = CreateQueryService(ctx);
        var spec = new AuditResourceHistorySpecification(
            tenantId, "Order", "order-1", afterKey: null, afterId: null, descending: false, take: 10);

        var results = await queryService.GetResourceHistoryAsync(spec);

        results.Should().HaveCount(2);
        results.Select(r => r.Action).Should().ContainInOrder("Created", "Approved");
        results.Should().OnlyContain(r => r.ResourceId == "order-1");
    }

    [Fact]
    public async Task GetActorActionsAsync_ReturnsOnlyMatchingActor()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();

        await CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"))
            .RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
        await CreateWriter(ctx, CreateActorContext(tenantId, "actor-2"))
            .RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-2" });

        var queryService = CreateQueryService(ctx);
        var spec = new AuditActorActionsSpecification(
            tenantId, "actor-1", afterKey: null, afterId: null, descending: false, take: 10);

        var results = await queryService.GetActorActionsAsync(spec);

        results.Should().ContainSingle();
        results[0].ActorId.Should().Be("actor-1");
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_UntamperedChain_IsIntact()
    {
        using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        var writer = CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"));

        await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
        await writer.RecordAsync(new AuditEntry { Action = "Approved", ResourceType = "Order", ResourceId = "order-1" });
        await writer.RecordAsync(new AuditEntry { Action = "Shipped", ResourceType = "Order", ResourceId = "order-1" });

        var result = await CreateQueryService(ctx).VerifyChainIntegrityAsync(
            tenantId, "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        result.IsIntact.Should().BeTrue();
        result.RecordsChecked.Should().Be(3);
        result.BrokenAtRecordId.Should().BeNull();
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_TamperedRecordHash_DetectedAsBroken()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        using (var ctx = CreateContext(databaseName))
        {
            var writer = CreateWriter(ctx, CreateActorContext(tenantId, "actor-1"));
            await writer.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
            await writer.RecordAsync(new AuditEntry { Action = "Approved", ResourceType = "Order", ResourceId = "order-1" });
        }

        Guid secondId;

        // Simulate an out-of-band tamper: a SEPARATE context, sharing the same backing store but
        // registered WITHOUT AuditRecordImmutabilityInterceptor, models a different/non-compliant
        // writer touching the data directly (e.g. a raw SQL console, a different service sharing
        // the database) — exactly the class of attack the hash chain exists to detect.
        using (var attackerCtx = CreateContext(databaseName, withImmutabilityGuard: false))
        {
            var second = await attackerCtx.AuditRecords.SingleAsync(r => r.Action == "Approved");
            secondId = second.Id;
            attackerCtx.Entry(second).CurrentValues["Action"] = "Tampered";
            await attackerCtx.SaveChangesAsync();
        }

        using var verifyCtx = CreateContext(databaseName);
        var result = await CreateQueryService(verifyCtx).VerifyChainIntegrityAsync(
            tenantId, "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        result.IsIntact.Should().BeFalse();
        result.BrokenAtRecordId.Should().Be(secondId);
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_EmptyRange_ReportsIntactWithZeroRecords()
    {
        using var ctx = CreateContext();

        var result = await CreateQueryService(ctx).VerifyChainIntegrityAsync(
            Guid.NewGuid(), "NothingHere", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        result.IsIntact.Should().BeTrue();
        result.RecordsChecked.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // C-164 — EfCoreAuditActorContext
    // -----------------------------------------------------------------------

    [Fact]
    public void EfCoreAuditActorContext_Authenticated_UsesUserIdFormat()
    {
        var userId = Guid.NewGuid();
        var userContext = TestDbContextFactory.CreateAuthenticatedUserContext(userId);
        var tenantProvider = TestDbContextFactory.CreateTenantProvider(Guid.NewGuid());

        var actorContext = new EfCoreAuditActorContext(
            userContext, tenantProvider, TestDbContextFactory.DefaultServiceOptions());

        actorContext.ActorId.Should().Be(userId.ToString("D"));
    }

    [Fact]
    public void EfCoreAuditActorContext_Unauthenticated_FallsBackToServiceName()
    {
        var userContext = TestDbContextFactory.CreateUnauthenticatedUserContext();
        var tenantProvider = TestDbContextFactory.CreateTenantProvider(Guid.Empty);

        var actorContext = new EfCoreAuditActorContext(
            userContext, tenantProvider, TestDbContextFactory.ServiceOptions("order-service"));

        actorContext.ActorId.Should().Be("order-service");
    }

    [Fact]
    public void EfCoreAuditActorContext_TenantId_DelegatesToTenantProvider()
    {
        var tenantId = Guid.NewGuid();
        var actorContext = new EfCoreAuditActorContext(
            TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid()),
            TestDbContextFactory.CreateTenantProvider(tenantId),
            TestDbContextFactory.DefaultServiceOptions());

        actorContext.TenantId.Should().Be(tenantId);
    }

    // -----------------------------------------------------------------------
    // T-134/T-135 — keyset seek predicate, never OFFSET/Skip.
    //
    // WO-071 tests. Deviates from the P-319 ToQueryString()/TagWith() SQL-text-capture convention
    // (see RepositoryTracingTests.GetQuery_AutomaticTagWith_AnnotatesGeneratedSqlWithSpecTypeName)
    // for a documented, verified reason: AuditRecord's queries must run against the InMemory
    // provider, not SQLite — SQLite cannot translate ORDER BY over a DateTimeOffset column
    // (AuditRecord.OccurredOn), and the real relational target (PostgreSQL, which orders by
    // timestamptz without issue) needs Testcontainers, unavailable in this environment (no running
    // Docker daemon). The InMemory provider never generates SQL text at all — ToQueryString() is
    // not meaningful against it. Instead, this proves the identical structural guarantee directly
    // against the LINQ expression tree GetKeysetQuery produces: no Queryable.Skip node exists
    // anywhere (keyset pagination never uses OFFSET), while a seek-predicate Where and a Take node
    // are both present once a cursor is supplied. This is the same claim ToQueryString would prove,
    // one level of translation earlier — proven for a build that only the InMemory provider can
    // execute end to end.
    // -----------------------------------------------------------------------

    [Fact]
    public void GetResourceHistoryQuery_WithCursor_NeverUsesSkip_UsesSeekPredicateAndTake()
    {
        using var ctx = CreateContext();
        var evaluator = new SpecificationEvaluator<AuditRecord>();
        var spec = new AuditResourceHistorySpecification(
            Guid.NewGuid(), "Order", "order-1",
            afterKey: DateTimeOffset.UtcNow, afterId: Guid.NewGuid(),
            descending: false, take: 10);

        var query = evaluator.GetKeysetQuery(ctx.Set<AuditRecord>().AsQueryable(), spec);

        ContainsMethodCallNamed(query.Expression, "Skip").Should().BeFalse(
            "keyset pagination must never use OFFSET-based Skip — the seek predicate " +
            "WHERE (OccurredOn, Id) > (@cursor, @cursorId) replaces it entirely");
        ContainsMethodCallNamed(query.Expression, "Where").Should().BeTrue(
            "a cursor was supplied, so a seek-predicate Where clause must be present");
        ContainsMethodCallNamed(query.Expression, "Take").Should().BeTrue();
    }

    [Fact]
    public void GetActorActionsQuery_WithCursor_NeverUsesSkip_UsesSeekPredicateAndTake()
    {
        using var ctx = CreateContext();
        var evaluator = new SpecificationEvaluator<AuditRecord>();
        var spec = new AuditActorActionsSpecification(
            Guid.NewGuid(), "actor-1",
            afterKey: DateTimeOffset.UtcNow, afterId: Guid.NewGuid(),
            descending: true, take: 10);

        var query = evaluator.GetKeysetQuery(ctx.Set<AuditRecord>().AsQueryable(), spec);

        ContainsMethodCallNamed(query.Expression, "Skip").Should().BeFalse(
            "keyset pagination must never use OFFSET-based Skip — the seek predicate replaces it");
        ContainsMethodCallNamed(query.Expression, "Where").Should().BeTrue();
        ContainsMethodCallNamed(query.Expression, "Take").Should().BeTrue();
    }

    [Fact]
    public void GetResourceHistoryQuery_FirstPage_NoSeekPredicate_ButStillNoSkip()
    {
        // No cursor supplied (first page) — GetKeysetQuery must skip the seek-predicate Where
        // entirely (WO-051/P-317 "skipped on the first page"), and must still never use Skip.
        using var ctx = CreateContext();
        var evaluator = new SpecificationEvaluator<AuditRecord>();
        var spec = new AuditResourceHistorySpecification(
            Guid.NewGuid(), "Order", "order-1", afterKey: null, afterId: null, descending: false, take: 10);

        var query = evaluator.GetKeysetQuery(ctx.Set<AuditRecord>().AsQueryable(), spec);

        ContainsMethodCallNamed(query.Expression, "Skip").Should().BeFalse();
        ContainsMethodCallNamed(query.Expression, "Take").Should().BeTrue();
    }

    private static bool ContainsMethodCallNamed(Expression expression, string methodName)
    {
        var finder = new MethodCallFinder(methodName);
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class MethodCallFinder(string methodName) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (string.Equals(node.Method.Name, methodName, StringComparison.Ordinal))
                Found = true;

            return base.VisitMethodCall(node);
        }
    }

    // -----------------------------------------------------------------------
    // T-136 — two different (tenant, resource-type) partitions chain independently.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VerifyChainIntegrityAsync_TamperedPartition_DoesNotAffect_UnrelatedPartitionVerification()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var ctx = CreateContext(databaseName))
        {
            var writerA = CreateWriter(ctx, CreateActorContext(tenantA, "actor-1"));
            await writerA.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Order", ResourceId = "order-1" });
            await writerA.RecordAsync(new AuditEntry { Action = "Approved", ResourceType = "Order", ResourceId = "order-1" });

            // A second, unrelated partition — different tenant AND different resource type — whose
            // chain must remain provably intact even after partition A is tampered with below.
            var writerB = CreateWriter(ctx, CreateActorContext(tenantB, "actor-2"));
            await writerB.RecordAsync(new AuditEntry { Action = "Created", ResourceType = "Invoice", ResourceId = "invoice-1" });
            await writerB.RecordAsync(new AuditEntry { Action = "Paid", ResourceType = "Invoice", ResourceId = "invoice-1" });
        }

        // Tamper only partition A's second record, bypassing the immutability interceptor exactly
        // as the existing single-partition tamper test does.
        using (var attackerCtx = CreateContext(databaseName, withImmutabilityGuard: false))
        {
            var tamperedA = await attackerCtx.AuditRecords.SingleAsync(r => r.Action == "Approved");
            attackerCtx.Entry(tamperedA).CurrentValues["Action"] = "Tampered";
            await attackerCtx.SaveChangesAsync();
        }

        using var verifyCtx = CreateContext(databaseName);
        var queryService = CreateQueryService(verifyCtx);

        var partitionAResult = await queryService.VerifyChainIntegrityAsync(
            tenantA, "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        var partitionBResult = await queryService.VerifyChainIntegrityAsync(
            tenantB, "Invoice", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        partitionAResult.IsIntact.Should().BeFalse("partition A was tampered with");
        partitionBResult.IsIntact.Should().BeTrue(
            "partition B is a wholly independent (TenantId, ResourceType) chain — tampering partition A " +
            "must never cause a false-positive break report against an unrelated partition");
        partitionBResult.RecordsChecked.Should().Be(2);
    }
}
