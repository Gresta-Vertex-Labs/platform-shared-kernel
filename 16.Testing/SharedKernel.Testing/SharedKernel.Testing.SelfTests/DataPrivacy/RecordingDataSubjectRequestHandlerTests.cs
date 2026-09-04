using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.DataPrivacy;

namespace SharedKernel.Testing.SelfTests.DataPrivacy;

/// <summary>
/// Proves <see cref="RecordingDataSubjectRequestHandler"/> against
/// <c>IDataSubjectRequestHandler</c>'s documented contract — no consuming domain has adopted this
/// fake yet, so this self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class RecordingDataSubjectRequestHandlerTests
{
    [Fact]
    public async Task ExportDataAsync_RecordsSubjectId_ReturnsSyntheticSuccess_WhenUnconfigured()
    {
        var handler = new RecordingDataSubjectRequestHandler();

        var result = await handler.ExportDataAsync("subject-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("subject-1", result.Value.SubjectId);
        handler.ShouldHaveExported("subject-1");
    }

    [Fact]
    public async Task RequestErasureAsync_RecordsSubjectId_ReturnsSyntheticSuccess_WhenUnconfigured()
    {
        var handler = new RecordingDataSubjectRequestHandler();

        var result = await handler.RequestErasureAsync("subject-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("subject-1", result.Value.SubjectId);
        handler.ShouldHaveErased("subject-1");
    }

    [Fact]
    public async Task ExportDataAsync_SetExportResult_SupportsFailureOutcome()
    {
        var handler = new RecordingDataSubjectRequestHandler();
        var failure = SharedKernel.Primitives.Results.Result<SharedKernel.DataPrivacy.DataSubjectRequests.DataSubjectExportBundle>.Failure(
            SharedKernel.Primitives.Errors.Error.NotFound("subject.unknown", "Subject not known to this service."));
        handler.SetExportResult("subject-2", failure);

        var result = await handler.ExportDataAsync("subject-2");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RequestErasureAsync_SetErasureResult_SupportsFailureOutcome()
    {
        var handler = new RecordingDataSubjectRequestHandler();
        var failure = SharedKernel.Primitives.Results.Result<SharedKernel.DataPrivacy.DataSubjectRequests.DataSubjectErasureReceipt>.Failure(
            SharedKernel.Primitives.Errors.Error.Conflict("subject.legal_hold", "Erasure blocked by an active legal hold."));
        handler.SetErasureResult("subject-3", failure);

        var result = await handler.RequestErasureAsync("subject-3");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ExportDataAsync_StampsTimestampFromInjectedClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2030, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var handler = new RecordingDataSubjectRequestHandler(clock);

        var result = await handler.ExportDataAsync("subject-1");

        Assert.Equal(clock.UtcNow, result.Value.ExportedAtUtc);
    }

    [Fact]
    public void ShouldHaveExported_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new RecordingDataSubjectRequestHandler().ShouldHaveExported("nobody"));

    [Fact]
    public void ShouldHaveErased_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new RecordingDataSubjectRequestHandler().ShouldHaveErased("nobody"));

    [Fact]
    public async Task ExportDataAsync_NullSubjectId_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new RecordingDataSubjectRequestHandler().ExportDataAsync(null!));
}
