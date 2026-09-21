using SharedKernel.Application.Auditing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Context;
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
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        var second = await writer.RecordAsync(Entry("Order", "order-1"));
        await writer.RecordAsync(Entry("Order", "order-2")); // different resource — excluded

        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 10);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Equal(2, history.Items.Count);
        Assert.Equal(first.Id, history.Items[0].Id);
        Assert.Equal(second.Id, history.Items[1].Id);
        Assert.False(history.HasMore);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_DifferentTenant_Excluded()
    {
        var writer = new FakeAuditTrailWriter(new FakeAuditActorContext(tenantId: Guid.NewGuid()));
        await writer.RecordAsync(Entry("Order", "order-1"));

        // A query service resolving a DIFFERENT tenant must see nothing — tenant scoping is applied
        // by the query service itself, never by the (tenant-agnostic) specification.
        var query = new FakeAuditQueryService(writer, new FakeAuditActorContext(tenantId: Guid.NewGuid()));
        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 10);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Empty(history.Items);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_Descending_ReturnsNewestFirst()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        var second = await writer.RecordAsync(Entry("Order", "order-1"));

        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: true, take: 10);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Equal(second.Id, history.Items[0].Id);
        Assert.Equal(first.Id, history.Items[1].Id);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_RespectsTake_AndReportsHasMore()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        for (var i = 0; i < 5; i++)
        {
            await writer.RecordAsync(Entry("Order", "order-1"));
        }

        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 2);

        var history = await query.GetResourceHistoryAsync(spec);

        Assert.Equal(2, history.Items.Count);
        Assert.True(history.HasMore);
    }

    [Fact]
    public async Task GetResourceHistoryAsync_CursorSeek_SkipsAlreadySeenPage()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        var second = await writer.RecordAsync(Entry("Order", "order-1"));
        var third = await writer.RecordAsync(Entry("Order", "order-1"));

        var firstPageSpec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 1);
        var firstPage = await query.GetResourceHistoryAsync(firstPageSpec);
        Assert.Equal(first.Id, firstPage.Items[0].Id);
        Assert.True(firstPage.HasMore);

        var secondPageSpec = new AuditResourceHistorySpecification(
            "Order", "order-1",
            afterSequence: firstPage.Items[0].Sequence, afterId: firstPage.Items[0].Id, descending: false, take: 10);
        var secondPage = await query.GetResourceHistoryAsync(secondPageSpec);

        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(second.Id, secondPage.Items[0].Id);
        Assert.Equal(third.Id, secondPage.Items[1].Id);
        Assert.False(secondPage.HasMore);
    }

    [Fact]
    public async Task GetActorActionsAsync_ReturnsOnlyMatchingActor()
    {
        var clock = new FakeClock();
        var actorContext = new FakeAuditActorContext(actorId: "actor-1");
        var writer = new FakeAuditTrailWriter(actorContext, clock);
        var query = new FakeAuditQueryService(writer, actorContext);

        var recorded = await writer.RecordAsync(Entry("Order", "order-1"));

        // Same writer/backing store, same tenant — a different actor's action must be excluded.
        actorContext.ActorId = "actor-2";
        clock.Advance(TimeSpan.FromMinutes(1));
        await writer.RecordAsync(Entry("Order", "order-2"));

        var spec = new AuditActorActionsSpecification(
            "actor-1", afterKey: null, afterId: null, descending: false, take: 10);

        var actions = await query.GetActorActionsAsync(spec);

        Assert.Single(actions.Items);
        Assert.Equal(recorded.Id, actions.Items[0].Id);
    }

    [Fact]
    public async Task GetResourceHistoryAcrossTenantsAsync_WithoutActiveScope_Throws()
    {
        var writer = new FakeAuditTrailWriter();
        var query = new FakeAuditQueryService(writer);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await query.GetResourceHistoryAcrossTenantsAsync("Order", "order-1", afterId: null, descending: false, take: 10));
    }

    [Fact]
    public async Task GetResourceHistoryAcrossTenantsAsync_WithActiveScope_SeesEveryTenant()
    {
        var writerA = new FakeAuditTrailWriter(new FakeAuditActorContext(tenantId: Guid.NewGuid()));
        var recordA = await writerA.RecordAsync(Entry("Order", "order-1"));

        var scope = new CrossTenantScope();
        var query = new FakeAuditQueryService(writerA, crossTenantScope: scope);

        using (scope.Enter())
        {
            var result = await query.GetResourceHistoryAcrossTenantsAsync(
                "Order", "order-1", afterId: null, descending: false, take: 10);

            Assert.Single(result.Items);
            Assert.Equal(recordA.Id, result.Items[0].Id);
        }
    }

    [Fact]
    public async Task ExportRangeAsync_StreamsChainInSequenceOrder()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        var second = await writer.RecordAsync(Entry("Order", "order-2"));

        var exported = new List<AuditRecord>();
        await foreach (var record in query.ExportRangeAsync(
            "Order", DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
        {
            exported.Add(record);
        }

        Assert.Equal(2, exported.Count);
        Assert.Equal(first.Id, exported[0].Id);
        Assert.Equal(second.Id, exported[1].Id);
    }

    [Fact]
    public async Task VerifyFullChainAsync_IntactChain_ReportsIntact()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        await writer.RecordAsync(Entry("Order", "order-1"));
        await writer.RecordAsync(Entry("Order", "order-2"));

        var result = await query.VerifyFullChainAsync("Order");

        Assert.True(result.IsIntact);
        Assert.Equal(2, result.RecordsChecked);
        Assert.Null(result.BrokenAtRecordId);
    }

    [Fact]
    public async Task VerifyFullChainAsync_TamperedRecordHash_ReportsBroken()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var record = await writer.RecordAsync(Entry("Order", "order-1"));
        var tampered = record with { ActorId = "someone-else" }; // RecordHash no longer matches recomputation
        writer.Seed([tampered]);

        var result = await query.VerifyFullChainAsync("Order");

        Assert.False(result.IsIntact);
        Assert.Equal(tampered.Id, result.BrokenAtRecordId);
    }

    [Fact]
    public async Task VerifyFullChainAsync_BrokenLink_ReportsBrokenAtSecondRecord()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        var second = await writer.RecordAsync(Entry("Order", "order-2"));

        // Re-seed the second record with a PreviousRecordHash that does not match the first's
        // RecordHash — a broken link, distinct from a tampered-content break. RecordHash is left
        // correctly recomputed so ONLY the link check (not the content-tamper check) can fire.
        var brokenPreviousHash = "not-the-real-previous-hash";
        var brokenSecond = second with { PreviousRecordHash = brokenPreviousHash };
        // Recompute RecordHash so the forged record is internally self-consistent — proving
        // VerifyFullChainAsync catches a broken CHAIN LINK specifically, not merely a
        // corrupted-field hash mismatch (see FakeAuditTrailWriter.ComputeHash's own remarks).
        brokenSecond = brokenSecond with { RecordHash = FakeAuditTrailWriter.ComputeHash(brokenSecond) };
        writer.Seed([first, brokenSecond]);

        var result = await query.VerifyFullChainAsync("Order");

        Assert.False(result.IsIntact);
        Assert.Equal(brokenSecond.Id, result.BrokenAtRecordId);
        Assert.Equal(2, result.RecordsChecked);
    }

    [Fact]
    public async Task VerifyFullChainAsync_GapInSequence_ReportsBroken()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);
        var query = new FakeAuditQueryService(writer, actorContext);

        var first = await writer.RecordAsync(Entry("Order", "order-1"));
        var second = await writer.RecordAsync(Entry("Order", "order-2"));

        // Delete the middle of a 3-record chain by seeding only records 1 and 3 (re-labelled 2 here
        // for a simple 2-record scenario): a gap must be detected even though every remaining
        // record's own hash and link are individually self-consistent.
        var thirdLookingLikeSecond = second with { Sequence = 3 };
        writer.Seed([first, thirdLookingLikeSecond with { RecordHash = FakeAuditTrailWriter.ComputeHash(thirdLookingLikeSecond) }]);

        var result = await query.VerifyFullChainAsync("Order");

        Assert.False(result.IsIntact);
        Assert.Equal(2, result.BrokenAtSequence);
    }

    [Fact]
    public async Task VerifyFullChainAsync_NoRecords_ReportsIntactZeroChecked()
    {
        var writer = new FakeAuditTrailWriter();
        var query = new FakeAuditQueryService(writer);

        var result = await query.VerifyFullChainAsync("Order");

        Assert.True(result.IsIntact);
        Assert.Equal(0, result.RecordsChecked);
    }

    [Fact]
    public async Task VerifyChainFromCheckpointAsync_NotSupported_Throws()
    {
        var writer = new FakeAuditTrailWriter();
        var query = new FakeAuditQueryService(writer);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await query.VerifyChainFromCheckpointAsync(
                new AuditChainCheckpoint
                {
                    Id = Guid.NewGuid(),
                    ResourceType = "Order",
                    Sequence = 1,
                    RecordHash = "hash",
                    CreatedOn = DateTimeOffset.UtcNow,
                    SigningKeyId = "test",
                    Signature = [],
                },
                null));
    }

    [Fact]
    public void Constructor_NullWriter_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeAuditQueryService(null!));

    private static AuditEntry Entry(string resourceType, string resourceId) => new()
    {
        Action = "Action",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Succeeded,
    };
}
