using FluentAssertions;
using SharedKernel.Execution.Auditing;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class FakeAuditTrailWriterTests
{
    private static AuditEntry Entry(string resourceType = "Order", string resourceId = "o-1", string? idempotencyKey = null) => new()
    {
        Action = "OrderApproved",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Succeeded,
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task RecordAsync_ResolvesIdentityAndTenantFromContext()
    {
        var tenant = Guid.NewGuid();
        var writer = new FakeAuditTrailWriter(new FakeAuditActorContext("alice", tenant) { SessionId = "s-1" });

        var record = await writer.RecordAsync(Entry());

        record.ActorId.Should().Be("alice");
        record.TenantId.Should().Be(tenant);
        record.SessionId.Should().Be("s-1");
        record.SourceService.Should().Be("fake-service");
    }

    [Fact]
    public async Task RecordAsync_UsesInjectedClock()
    {
        var clock = new FakeClock();
        var writer = new FakeAuditTrailWriter(clock: clock);

        (await writer.RecordAsync(Entry())).OccurredOn.Should().Be(clock.UtcNow);
    }

    [Fact]
    public async Task RecordAsync_AssignsPerChainSequence()
    {
        var writer = new FakeAuditTrailWriter();

        (await writer.RecordAsync(Entry())).Sequence.Should().Be(1);
        (await writer.RecordAsync(Entry())).Sequence.Should().Be(2);
        (await writer.RecordAsync(Entry(resourceType: "Customer"))).Sequence.Should().Be(1);
    }

    [Fact]
    public async Task RecordAsync_SameIdempotencyKey_ReturnsExistingRecord()
    {
        var writer = new FakeAuditTrailWriter();

        var first = await writer.RecordAsync(Entry(idempotencyKey: "k"));
        var second = await writer.RecordAsync(Entry(idempotencyKey: "k"));

        second.Should().Be(first);
        writer.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordAsync_SimulateFailure_ThrowsAndRecordsNothing()
    {
        var writer = new FakeAuditTrailWriter { SimulateFailure = true };

        await writer.Invoking(w => w.RecordAsync(Entry())).Should().ThrowAsync<InvalidOperationException>();
        writer.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task ErasePayload_ClearsSnapshotsOnce()
    {
        var writer = new FakeAuditTrailWriter();
        var record = await writer.RecordAsync(Entry() with { AfterSnapshot = "{}" });

        writer.ErasePayload(record.Id).Should().BeTrue();
        writer.ErasePayload(record.Id).Should().BeFalse();
        writer.Records.Single().Should().Match<SharedKernel.Persistence.EfCore.Auditing.AuditRecord>(r => r.PayloadErased && r.AfterSnapshot == null);
    }

    [Fact]
    public async Task Records_IsASnapshot_AndResetClears()
    {
        var writer = new FakeAuditTrailWriter();
        await writer.RecordAsync(Entry());
        var snapshot = writer.Records;

        writer.Reset();

        snapshot.Should().ContainSingle();
        writer.Records.Should().BeEmpty();
    }
}
