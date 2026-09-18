using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.DataPrivacy;

namespace SharedKernel.Testing.SelfTests.DataPrivacy;

/// <summary>
/// Proves <see cref="RecordingDataSubjectRequestHandler"/> against
/// <see cref="IDataSubjectRequestHandler"/>'s documented contract.
/// </summary>
public sealed class RecordingDataSubjectRequestHandlerTests
{
    private static readonly DateTimeOffset At = new(2030, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static DataSubjectRequest Request(string requestId = "req-1", string subjectId = "subject-1") =>
        new(requestId, subjectId, At);

    [Fact]
    public async Task ExportAsync_Unconfigured_ReturnsAnEmptyExportAndRecordsTheRequest()
    {
        var handler = new RecordingDataSubjectRequestHandler(source: "orders-api");

        var result = await handler.ExportAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Records);
        Assert.Equal("orders-api", result.Value.Source);
        Assert.Equal(Request(), Assert.Single(handler.ExportRequests));
        handler.ShouldHaveExported("subject-1");
    }

    [Fact]
    public async Task EraseAsync_Unconfigured_ReturnsACompleteEmptyReceipt()
    {
        var handler = new RecordingDataSubjectRequestHandler();

        var result = await handler.EraseAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        Assert.Equal((0, 0), (result.Value.ErasedRecords, result.Value.AnonymizedRecords));
        handler.ShouldHaveErased("subject-1");
    }

    [Fact]
    public async Task EraseAsync_SameRequestIdAgain_ReturnsTheFirstOutcome()
    {
        var clock = new FakeClock(At);
        var handler = new RecordingDataSubjectRequestHandler(clock);

        var first = await handler.EraseAsync(Request());
        clock.Advance(TimeSpan.FromHours(1));
        var second = await handler.EraseAsync(Request());

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(2, handler.ErasureRequests.Count);
    }

    [Fact]
    public async Task EraseAsync_SetErasureResult_ReturnsTheConfiguredReceipt()
    {
        var handler = new RecordingDataSubjectRequestHandler();
        var receipt = new DataSubjectErasureReceipt(Request(subjectId: "subject-2"), "billing-api", At, 4, 0,
            [new RetainedData("invoices", "Tax Procedure Law 213, Art. 253", At.AddYears(5))]);
        handler.SetErasureResult("subject-2", receipt);

        var result = await handler.EraseAsync(Request(subjectId: "subject-2"));

        Assert.False(result.Value.IsComplete);
        Assert.Equal("invoices", Assert.Single(result.Value.Retained).Category);
    }

    [Fact]
    public async Task ExportAsync_SetExportResult_SupportsAFailure()
    {
        var handler = new RecordingDataSubjectRequestHandler();
        handler.SetExportResult("subject-3", SharedKernel.Primitives.Results.Result<DataSubjectExport>.Failure(
            SharedKernel.Primitives.Errors.Error.Unexpected(DataPrivacyErrorCodes.TemporarilyUnavailable, "A restore is running.")));

        var result = await handler.ExportAsync(Request(subjectId: "subject-3"));

        Assert.Equal(DataPrivacyErrorCodes.TemporarilyUnavailable, result.Error.Code);
    }

    [Fact]
    public async Task ExportAsync_StampsTheTimeFromTheInjectedClock()
    {
        var clock = new FakeClock(At);
        var handler = new RecordingDataSubjectRequestHandler(clock);

        var result = await handler.ExportAsync(Request());

        Assert.Equal(At, result.Value.ExportedAt);
    }

    [Fact]
    public void ShouldHaveExported_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new RecordingDataSubjectRequestHandler().ShouldHaveExported("nobody"));

    [Fact]
    public void ShouldHaveErased_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new RecordingDataSubjectRequestHandler().ShouldHaveErased("nobody"));

    [Fact]
    public async Task ExportAsync_NullRequest_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new RecordingDataSubjectRequestHandler().ExportAsync(null!));
}
