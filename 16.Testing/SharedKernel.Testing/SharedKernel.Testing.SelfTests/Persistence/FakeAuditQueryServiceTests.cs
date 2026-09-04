using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeAuditQueryService"/> against <c>06.Persistence.Abstractions</c>'s
/// <c>IAuditQueryService</c> contract — no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeAuditQueryServiceTests
{
    [Fact]
    public async Task GetResourceHistoryAsync_ReturnsOnlyMatchingResource_OrderedOldestFirst()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        await writer.RecordAsync(Entry("Order", "order-2")); // different resource — excluded

        var spec = new AuditResourceHistorySpecification(
            actorContext.TenantId, "Order", "order-1", afterKey: null, afterId: null, descending: false, take: 10);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Equal(2, history.Count);
        Assert.Equal(first.Id, history[0].Id);
        Assert.Equal(second.Id, history[1].Id);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_Descending_ReturnsNewestFirst()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await writer.RecordAsync(Entry("Order", "order-1"));

        var spec = new AuditResourceHistorySpecification(
            actorContext.TenantId, "Order", "order-1", afterKey: null, afterId: null, descending: true, take: 10);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Equal(second.Id, history[0].Id);
        Assert.Equal(first.Id, history[1].Id);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_RespectsTake()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        for (var i = 0; i < 5; i++)
        {
            await writer.RecordAsync(Entry("Order", "order-1"));
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var spec = new AuditResourceHistorySpecification(
            actorContext.TenantId, "Order", "order-1", afterKey: null, afterId: null, descending: false, take: 2);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_CursorSeek_SkipsAlreadySeenPage()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        var third = await writer.RecordAsync(Entry("Order", "order-1"));

        var firstPageSpec = new AuditResourceHistorySpecification(
            actorContext.TenantId, "Order", "order-1", afterKey: null, afterId: null, descending: false, take: 1);
        var firstPage = await query.GetResourceHistoryAsync(firstPageSpec);
        Assert.Equal(first.Id, firstPage[0].Id);

        var secondPageSpec = new AuditResourceHistorySpecification(
            actorContext.TenantId, "Order", "order-1",
            afterKey: firstPage[0].OccurredOn, afterId: firstPage[0].Id, descending: false, take: 10);
        var secondPage = await query.GetResourceHistoryAsync(secondPageSpec);

        Assert.Equal(2, secondPage.Count);
        Assert.Equal(second.Id, secondPage[0].Id);
        Assert.Equal(third.Id, secondPage[1].Id);
    }

    [Fact]
    public async Task GetActorActionsAsync_ReturnsOnlyMatchingActor()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext(actorId: "actor-1");
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        var recorded = await writer.RecordAsync(Entry("Order", "order-1"));

        // Same writer/backing store, same tenant — a different actor's action must be excluded.
        actorContext.ActorId = "actor-2";
        clock.Advance(TimeSpan.FromMinutes(1));
        await writer.RecordAsync(Entry("Order", "order-2"));

        var spec = new AuditActorActionsSpecification(
            actorContext.TenantId, "actor-1", afterKey: null, afterId: null, descending: false, take: 10);

        var actions = await query.GetActorActionsAsync(spec);

        Assert.Single(actions);
        Assert.Equal(recorded.Id, actions[0].Id);
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_IntactChain_ReportsIntact()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        await writer.RecordAsync(Entry("Order", "order-2"));

        var result = await query.VerifyChainIntegrityAsync(
            actorContext.TenantId, "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.True(result.IsIntact);
        Assert.Equal(2, result.RecordsChecked);
        Assert.Null(result.BrokenAtRecordId);
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_TamperedRecordHash_ReportsBroken()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer);

        var record = await writer.RecordAsync(Entry("Order", "order-1"));
        var tampered = record with { ActorId = "someone-else" }; // RecordHash no longer matches recomputation
        writer.Seed([tampered]);

        var result = await query.VerifyChainIntegrityAsync(
            actorContext.TenantId, "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.False(result.IsIntact);
        Assert.Equal(tampered.Id, result.BrokenAtRecordId);
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_BrokenLink_ReportsBrokenAtSecondRecord()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await writer.RecordAsync(Entry("Order", "order-2"));

        // Re-seed the second record with a PreviousRecordHash that does not match the first's
        // RecordHash — a broken link, distinct from a tampered-content break. RecordHash is left
        // correctly recomputed so ONLY the link check (not the content-tamper check) can fire.
        var brokenPreviousHash = "not-the-real-previous-hash";
        var brokenSecond = second with { PreviousRecordHash = brokenPreviousHash };
        // Recompute RecordHash so the forged record is internally self-consistent — proving
        // VerifyChainIntegrityAsync catches a broken CHAIN LINK specifically, not merely a
        // corrupted-field hash mismatch (see FakeAuditTrailWriter.ComputeHash's own remarks).
        brokenSecond = brokenSecond with { RecordHash = FakeAuditTrailWriter.ComputeHash(brokenSecond) };
        writer.Seed([first, brokenSecond]);

        var result = await query.VerifyChainIntegrityAsync(
            actorContext.TenantId, "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.False(result.IsIntact);
        Assert.Equal(brokenSecond.Id, result.BrokenAtRecordId);
        Assert.Equal(2, result.RecordsChecked);
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_NoRecordsInRange_ReportsIntactZeroChecked()
    {
        var writer = new FakeAuditTrailWriter();
        var query = new FakeAuditQueryService(writer);

        var result = await query.VerifyChainIntegrityAsync(
            Guid.NewGuid(), "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        Assert.True(result.IsIntact);
        Assert.Equal(0, result.RecordsChecked);
    }

    [Fact]
    public void Constructor_NullWriter_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeAuditQueryService(null!));

    private static AuditEntry Entry(string resourceType, string resourceId) => new()
    {
        Action = "Action",
        ResourceType = resourceType,
        ResourceId = resourceId,
    };
}
