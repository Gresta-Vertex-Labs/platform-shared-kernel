using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Testing.Application;

namespace SharedKernel.Testing.SelfTests.Application;

/// <summary>
/// Proves <see cref="FakeAuditTrailWriter"/> against <c>05.Application.Behaviors</c>'s local-seam
/// <c>IAuditTrailWriter</c> contract — no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeAuditTrailWriterTests
{
    private static AuditEntry CreateEntry(string action = "OrderApproved", string resourceType = "Order", string resourceId = "order-1") =>
        new(action, resourceType, resourceId, "before", "after", "approval-1");

    [Fact]
    public async Task RecordAsync_RecordsEntry()
    {
        var writer = new FakeAuditTrailWriter();
        var entry = CreateEntry();

        await writer.RecordAsync(entry);

        Assert.Single(writer.Recorded);
        Assert.Same(entry, writer.Recorded[0]);
    }

    [Fact]
    public async Task RecordAsync_NullEntry_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await new FakeAuditTrailWriter().RecordAsync(null!));

    [Fact]
    public async Task RecordAsync_SimulateFailure_Throws_NeverRecords()
    {
        var writer = new FakeAuditTrailWriter { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.RecordAsync(CreateEntry()));
        Assert.Empty(writer.Recorded);
    }

    [Fact]
    public async Task ShouldHaveAudited_MatchingEntry_ReturnsIt()
    {
        var writer = new FakeAuditTrailWriter();
        var entry = CreateEntry();
        await writer.RecordAsync(entry);

        var found = writer.ShouldHaveAudited("OrderApproved", "Order", "order-1");

        Assert.Same(entry, found);
    }

    [Fact]
    public void ShouldHaveAudited_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new FakeAuditTrailWriter().ShouldHaveAudited("x", "y", "z"));

    [Fact]
    public async Task Reset_ClearsRecordedEntries_NotSimulateFailure()
    {
        var writer = new FakeAuditTrailWriter { SimulateFailure = false };
        await writer.RecordAsync(CreateEntry());
        writer.SimulateFailure = true;

        writer.Reset();

        Assert.Empty(writer.Recorded);
        Assert.True(writer.SimulateFailure);
    }

    [Fact]
    public async Task RecordAsync_NullApprovalId_IsRecordedAsNull()
    {
        var writer = new FakeAuditTrailWriter();
        var entry = new AuditEntry("Action", "Type", "Id", null, null, null);

        await writer.RecordAsync(entry);

        Assert.Null(writer.Recorded[0].ApprovalId);
    }
}
