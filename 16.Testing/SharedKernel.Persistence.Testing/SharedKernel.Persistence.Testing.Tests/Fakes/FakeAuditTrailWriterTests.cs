using SharedKernel.Application.Auditing;
using SharedKernel.Testing.Application;
using SharedKernel.Persistence.Testing;

namespace SharedKernel.Persistence.Testing.Tests.Fakes;

/// <summary>
/// Proves <see cref="FakeAuditTrailWriter"/> against the shared
/// <c>IAuditTrailWriter</c> contract — no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class FakeAuditTrailWriterTests
{
    private static AuditEntry CreateEntry(
        string action = "OrderApproved",
        string resourceType = "Order",
        string resourceId = "order-1",
        bool succeeded = true,
        string? errorCode = null) =>
        new()
        {
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            BeforeSnapshot = "before",
            AfterSnapshot = "after",
            Outcome = succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed,
            ErrorCode = errorCode,
        };

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
    public async Task RecordAsync_SucceededTrue_ErrorCodeIsNull()
    {
        var writer = new FakeAuditTrailWriter();
        var entry = CreateEntry(succeeded: true, errorCode: null);

        await writer.RecordAsync(entry);

        Assert.Equal(AuditOutcome.Succeeded, writer.Recorded[0].Outcome);
        Assert.Null(writer.Recorded[0].ErrorCode);
    }

    [Fact]
    public async Task RecordAsync_SucceededFalse_RecordsErrorCode()
    {
        var writer = new FakeAuditTrailWriter();
        var entry = CreateEntry(succeeded: false, errorCode: "order.already_approved");

        await writer.RecordAsync(entry);

        Assert.Equal(AuditOutcome.Failed, writer.Recorded[0].Outcome);
        Assert.Equal("order.already_approved", writer.Recorded[0].ErrorCode);
    }
}
