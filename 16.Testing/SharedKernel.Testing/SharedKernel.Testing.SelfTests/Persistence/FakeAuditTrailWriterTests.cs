using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeAuditTrailWriter"/> against <c>06.Persistence.Abstractions</c>'s
/// <c>IAuditTrailWriter</c> contract — no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeAuditTrailWriterTests
{
    private static AuditEntry CreateEntry(
        string action = "OrderApproved",
        string resourceType = "Order",
        string resourceId = "order-1",
        string? idempotencyKey = null,
        AuditOutcome outcome = AuditOutcome.Succeeded) =>
        new()
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeSnapshot = "before",
            AfterSnapshot = "after",
            ApprovalId = "approval-1",
            Outcome = outcome,
            IdempotencyKey = idempotencyKey,
        };

    [Fact]
    public async Task RecordAsync_ResolvesActorAndTenantFromActorContext()
    {
        var actorContext = new FakeAuditActorContext("actor-1", Guid.NewGuid());
        var writer = new FakeAuditTrailWriter(actorContext);

        var record = await writer.RecordAsync(CreateEntry());

        Assert.Equal("actor-1", record.ActorId);
        Assert.Equal(ActorKind.User, record.ActorKind);
        Assert.Equal(actorContext.TenantId, record.TenantId);
    }

    [Fact]
    public async Task RecordAsync_NoTenantResolved_RecordsNullTenant()
    {
        // FakeAuditActorContext's constructor `tenantId: null` means "use the default", per its own
        // documented convention — TenantId must be set to null via the property SETTER, after
        // construction, to simulate "no tenant resolved".
        var actorContext = new FakeAuditActorContext { TenantId = null };
        var writer = new FakeAuditTrailWriter(actorContext);

        var record = await writer.RecordAsync(CreateEntry());

        Assert.Null(record.TenantId);
    }

    [Fact]
    public async Task RecordAsync_ResolvesTimestampFromInjectedClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var writer = new FakeAuditTrailWriter(clock: clock);

        var record = await writer.RecordAsync(CreateEntry());

        Assert.Equal(clock.UtcNow, record.OccurredOn);
    }

    [Fact]
    public async Task RecordAsync_AssignsFreshGuidPerCall()
    {
        var writer = new FakeAuditTrailWriter();

        var first = await writer.RecordAsync(CreateEntry());
        var second = await writer.RecordAsync(CreateEntry());

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task RecordAsync_FirstRecordInChain_HasNullPreviousHash_AndSequenceOne()
    {
        var writer = new FakeAuditTrailWriter();

        var record = await writer.RecordAsync(CreateEntry());

        Assert.Null(record.PreviousRecordHash);
        Assert.Equal(1, record.Sequence);
        Assert.False(string.IsNullOrWhiteSpace(record.RecordHash));
    }

    [Fact]
    public async Task RecordAsync_SecondRecordInSameChain_ChainsToFirst_SequenceIncrements()
    {
        var actorContext = new FakeAuditActorContext();
        var writer = new FakeAuditTrailWriter(actorContext);

        var first = await writer.RecordAsync(CreateEntry(resourceId: "order-1"));
        var second = await writer.RecordAsync(CreateEntry(resourceId: "order-2"));

        Assert.Equal(first.RecordHash, second.PreviousRecordHash);
        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public async Task RecordAsync_DifferentResourceType_StartsNewChain()
    {
        var writer = new FakeAuditTrailWriter();

        await writer.RecordAsync(CreateEntry(resourceType: "Order"));
        var invoiceRecord = await writer.RecordAsync(CreateEntry(resourceType: "Invoice"));

        Assert.Null(invoiceRecord.PreviousRecordHash);
        Assert.Equal(1, invoiceRecord.Sequence);
    }

    [Fact]
    public async Task RecordAsync_DifferentTenant_StartsNewChain()
    {
        var actorContextA = new FakeAuditActorContext(tenantId: Guid.NewGuid());
        var actorContextB = new FakeAuditActorContext(tenantId: Guid.NewGuid());
        var writerA = new FakeAuditTrailWriter(actorContextA);
        var writerB = new FakeAuditTrailWriter(actorContextB);

        await writerA.RecordAsync(CreateEntry());
        var recordB = await writerB.RecordAsync(CreateEntry());

        // Independent writer instances never share a chain, but even against a shared backing
        // store the (TenantId, ResourceType) chain key means tenant B's chain starts fresh.
        Assert.Null(recordB.PreviousRecordHash);
    }

    [Fact]
    public async Task RecordAsync_SameIdempotencyKeyTwice_ReturnsSameRecord_NeverAppendsDuplicate()
    {
        var writer = new FakeAuditTrailWriter();

        var first = await writer.RecordAsync(CreateEntry(idempotencyKey: "idem-1"));
        var second = await writer.RecordAsync(CreateEntry(idempotencyKey: "idem-1"));

        Assert.Equal(first.Id, second.Id);
        Assert.Single(writer.Records);
    }

    [Fact]
    public async Task RecordAsync_DifferentIdempotencyKeys_BothAppend()
    {
        var writer = new FakeAuditTrailWriter();

        await writer.RecordAsync(CreateEntry(idempotencyKey: "idem-1"));
        await writer.RecordAsync(CreateEntry(idempotencyKey: "idem-2"));

        Assert.Equal(2, writer.Records.Count);
    }

    [Fact]
    public async Task RecordAsync_FailedOutcome_RecordsErrorCode()
    {
        var writer = new FakeAuditTrailWriter();

        var entry = CreateEntry(outcome: AuditOutcome.Failed) with { ErrorCode = "order.rejected" };
        var record = await writer.RecordAsync(entry);

        Assert.Equal(AuditOutcome.Failed, record.Outcome);
        Assert.Equal("order.rejected", record.ErrorCode);
    }

    [Fact]
    public async Task RecordAsync_SimulateFailure_Throws_NeverRecords()
    {
        var writer = new FakeAuditTrailWriter { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.RecordAsync(CreateEntry()));
        Assert.Empty(writer.Records);
    }

    [Fact]
    public async Task RecordAsync_NullEntry_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new FakeAuditTrailWriter().RecordAsync(null!));

    [Fact]
    public async Task Records_ReturnsFreshSnapshot_EachCall_NeverTheSameLiveCollection()
    {
        var writer = new FakeAuditTrailWriter();
        await writer.RecordAsync(CreateEntry());

        var first = writer.Records;
        var second = writer.Records;

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IAuditTrailWriter_HasNoUpdateOrDeleteMember()
    {
        // Structural proof: the interface exposes exactly one member, RecordAsync — there is no
        // update/delete method to guard against calling.
        var members = typeof(IAuditTrailWriter).GetMethods();

        Assert.Single(members);
        Assert.Equal("RecordAsync", members[0].Name);
    }

    [Fact]
    public async Task Seed_ReplacesBackingStore()
    {
        var writer = new FakeAuditTrailWriter();
        await writer.RecordAsync(CreateEntry());

        var seeded = new AuditRecord
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ActorId = "seeded-actor",
            ActorKind = ActorKind.System,
            Action = "Seeded",
            ResourceType = "Seed",
            ResourceId = "seed-1",
            Sequence = 1,
            OccurredOn = DateTimeOffset.UtcNow,
            Outcome = AuditOutcome.Succeeded,
            HashAlgorithm = "FAKE-NONCRYPTOGRAPHIC",
            SchemaVersion = 1,
            KeyId = "fake",
            RecordHash = "deadbeef",
        };
        writer.Seed([seeded]);

        Assert.Single(writer.Records);
        Assert.Same(seeded, writer.Records[0]);
    }

    [Fact]
    public async Task Reset_ClearsRecords_NotSimulateFailure()
    {
        var writer = new FakeAuditTrailWriter { SimulateFailure = false };
        await writer.RecordAsync(CreateEntry());
        writer.SimulateFailure = true;

        writer.Reset();

        Assert.Empty(writer.Records);
        Assert.True(writer.SimulateFailure);
    }
}
