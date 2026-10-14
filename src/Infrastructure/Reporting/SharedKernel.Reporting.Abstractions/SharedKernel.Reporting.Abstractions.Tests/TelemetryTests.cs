using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>Spans and instruments of every export and conversion, asserted through the shared telemetry helpers.</summary>
public sealed class TelemetryTests : IDisposable
{
    private const string Duration = "sharedkernel.reporting.operation.duration";
    private const string RowsInstrument = "sharedkernel.reporting.rows";
    private const string BytesInstrument = "sharedkernel.reporting.bytes";

    private readonly ActivityRecorder _spans = ActivityRecorder.StartRecording(ReportingDiagnostics.ActivitySourceName);
    private readonly MetricRecorder _metrics = MetricRecorder.StartRecording(ReportingDiagnostics.MeterName);
    private readonly RecordingMeterFactory _meterFactory = new();

    public void Dispose()
    {
        _spans.Dispose();
        _metrics.Dispose();
        _meterFactory.Dispose();
    }

    [Fact]
    public void Names_AreThePinnedLiterals()
    {
        ReportingDiagnostics.ActivitySourceName.Should().Be("SharedKernel.Reporting");
        ReportingDiagnostics.MeterName.Should().Be("SharedKernel.Reporting");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Export_Success_RecordsTheSpanAndEveryInstrument_WithOrWithoutAMeterFactory(bool withFactory)
    {
        var exporter = new LineExporter<Row>(Dependencies(withFactory));
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await exporter.ExportToStreamAsync(Rows.Generate(3), Rows.Definition, stream);

        Activity span = _spans.RecordedActivities.ShouldHaveSingleSpan("reporting export");
        span.ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveSource(ReportingDiagnostics.ActivitySourceName)
            .ShouldHaveTag("reporting.format", "lines")
            .ShouldHaveTag("reporting.operation", "export")
            .ShouldHaveTag("reporting.row_count", 3L)
            .ShouldHaveTag("reporting.size_bytes", result.Value.SizeBytes)
            .ShouldNotHaveTag("reporting.store")
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();

        _metrics
            .ShouldHaveInstrument(Duration, "s", InstrumentKind.Histogram)
            .ShouldHaveInstrument(RowsInstrument, "{row}", InstrumentKind.Counter)
            .ShouldHaveInstrument(BytesInstrument, "By", InstrumentKind.Counter)
            .ShouldHaveTagSet(Duration, "reporting.format", "reporting.operation", "outcome")
            .ShouldHaveTagSet(RowsInstrument, "reporting.format")
            .ShouldHaveTagSet(BytesInstrument, "reporting.format", "reporting.operation")
            .ShouldHaveMeasurement(Duration, null, Tag("outcome", WellKnownOutcomes.Success), Tag("reporting.operation", "export"))
            .ShouldHaveSum(RowsInstrument, 3, Tag("reporting.format", "lines"))
            .ShouldHaveSum(BytesInstrument, result.Value.SizeBytes, Tag("reporting.format", "lines"))
            .ShouldUseOutcomeVocabulary(Duration);
        _metrics.MeasurementsOf(Duration).Should().ContainSingle();
        _metrics.MeasurementsOf(RowsInstrument).Should().ContainSingle();
    }

    [Fact]
    public async Task Export_ToAStore_TagsTheSpanWithTheStoreName()
    {
        var exporter = new LineExporter<Row>(StoreDependencies());

        await exporter.ExportAsync(Rows.Generate(2), Rows.Definition, new ReportDestination { Store = "reports", Key = "out.txt" });

        _spans.RecordedActivities.ShouldHaveSingleSpan("reporting export")
            .ShouldHaveTag("reporting.store", "reports")
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task Export_ReturningAFailure_RecordsTheErrorTypeNotTheCode()
    {
        var exporter = new LineExporter<Row>(Dependencies(withFactory: true)) { FailAfterRows = 0 };
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await exporter.ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        result.IsFailure.Should().BeTrue();
        Activity span = _spans.RecordedActivities.ShouldHaveSingleSpan("reporting export");
        span.ShouldBeRecordedFailure(result.Error);
        span.GetTagItem("error.type").Should().Be("Validation").And.NotBe(ReportingErrorCodes.RowLimitExceeded);
        span.GetTagItem("error.code").Should().Be(ReportingErrorCodes.RowLimitExceeded);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(ReportingErrorCodes.RowLimitExceeded);

        _metrics.ShouldHaveMeasurement(Duration, null, Tag("outcome", WellKnownOutcomes.Failure), Tag("error.type", "Validation"));
        _metrics.ShouldNotHaveTagKey(Duration, "error.code");
        _metrics.MeasurementsOf(RowsInstrument).Should().BeEmpty();
        _metrics.MeasurementsOf(BytesInstrument).Should().BeEmpty();
    }

    [Fact]
    public async Task Export_ThrowingFromTheRowSource_RecordsTheExceptionEventAndItsFullName()
    {
        var exporter = new LineExporter<Row>(Dependencies(withFactory: true)) { ThrowAtRow = 0 };
        using var stream = new MemoryStream();

        Func<Task> export = () => exporter.ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream);

        await export.Should().ThrowAsync<InvalidOperationException>();
        _spans.RecordedActivities.ShouldHaveSingleSpan("reporting export")
            .ShouldBeRecordedException(typeof(InvalidOperationException));
        _metrics.ShouldHaveMeasurement(
            Duration,
            null,
            Tag("outcome", WellKnownOutcomes.Exception),
            Tag("error.type", typeof(InvalidOperationException).FullName));
        _metrics.MeasurementsOf(RowsInstrument).Should().BeEmpty();
    }

    [Fact]
    public async Task Export_Cancelled_IsNeitherAnErrorNorAnErrorType()
    {
        var exporter = new LineExporter<Row>(Dependencies(withFactory: true));
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> export = () => exporter.ExportToStreamAsync(Rows.Generate(2), Rows.Definition, stream, cancellation.Token);

        await export.Should().ThrowAsync<OperationCanceledException>();
        _spans.RecordedActivities.ShouldHaveSingleSpan("reporting export").ShouldBeRecordedCancellation();
        _metrics.ShouldHaveMeasurement(Duration, null, Tag("outcome", WellKnownOutcomes.Canceled));
        _metrics.ShouldNotHaveTagKey(Duration, "error.type");
        _metrics.MeasurementsOf(RowsInstrument).Should().BeEmpty();
    }

    [Fact]
    public async Task Export_FailingStorage_RecordsTheStorageErrorTypeOnTheSpanAndTheMetric()
    {
        var exporter = new LineExporter<Row>(StoreDependencies(failUpload: true));

        Result<ReportExportOutcome> result = await exporter.ExportAsync(
            Rows.Generate(1),
            Rows.Definition,
            new ReportDestination { Store = "reports", Key = "out.txt" });

        result.IsFailure.Should().BeTrue();
        _spans.RecordedActivities.ShouldHaveSingleSpan("reporting export").ShouldBeRecordedFailure(result.Error);
        _metrics.ShouldHaveMeasurement(
            Duration,
            null,
            Tag("outcome", WellKnownOutcomes.Failure),
            Tag("error.type", result.Error.Type.ToString()));
    }

    [Fact]
    public async Task Convert_Success_RecordsTheConvertSpanAndBytesButNoRows()
    {
        var converter = new TextConverter(Dependencies(withFactory: true));
        using var stream = new MemoryStream();

        Result<long> result = await converter.ConvertToStreamAsync("<p>x</p>", stream);

        result.IsSuccess.Should().BeTrue();
        _spans.RecordedActivities.ShouldHaveSingleSpan("reporting convert")
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveTag("reporting.operation", "convert")
            .ShouldHaveTag("reporting.format", "pdf")
            .ShouldHaveTag("reporting.size_bytes", 4L)
            .ShouldNotHaveTag("reporting.row_count")
            .ShouldBeRecordedSuccess();
        _metrics.ShouldHaveSum(BytesInstrument, 4, Tag("reporting.operation", "convert"));
        _metrics.MeasurementsOf(RowsInstrument).Should().BeEmpty();
        _metrics.ShouldHaveMeasurement(Duration, null, Tag("outcome", WellKnownOutcomes.Success), Tag("reporting.operation", "convert"));
    }

    [Fact]
    public async Task Convert_Failure_And_Exception_UseTheSameShapesAsExport()
    {
        Error error = ReportingErrors.ConverterUnavailable();
        var failing = new TextConverter(Dependencies(withFactory: true)) { Failure = error };
        var throwing = new TextConverter(Dependencies(withFactory: true)) { Throws = true };
        using var stream = new MemoryStream();

        Result<long> failure = await failing.ConvertToStreamAsync("<p>x</p>", stream);
        Func<Task> thrown = () => throwing.ConvertToStreamAsync("<p>x</p>", stream);

        failure.IsFailure.Should().BeTrue();
        await thrown.Should().ThrowAsync<InvalidOperationException>();
        IReadOnlyList<Activity> spans = _spans.RecordedActivities;
        spans.Should().HaveCount(2);
        spans[0].ShouldBeRecordedFailure(error);
        spans[0].GetTagItem("error.type").Should().Be("Unavailable");
        spans[1].ShouldBeRecordedException(typeof(InvalidOperationException));
    }

    [Fact]
    public async Task AnUnlistenedSource_DoesNotThrow()
    {
        _spans.Dispose();
        var exporter = new LineExporter<Row>(Dependencies(withFactory: false));
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await exporter.ExportToStreamAsync(Rows.Generate(1), Rows.Definition, stream);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void TheMeterFactory_IsAskedForTheReportingMeter_WithTheAssemblyVersion()
    {
        _ = Dependencies(withFactory: true);

        System.Diagnostics.Metrics.MeterOptions options = _meterFactory.Requested.Should().ContainSingle().Subject;
        options.Name.Should().Be(ReportingDiagnostics.MeterName);
        options.Version.Should().Be(TelemetryVersion.Of(typeof(ReportingDependencies).Assembly));
    }

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    private ReportingDependencies Dependencies(bool withFactory) =>
        new(NullLogger<ReportingDependencies>.Instance, meterFactory: withFactory ? _meterFactory : null);

    private ReportingDependencies StoreDependencies(bool failUpload = false) =>
        new(NullLogger<ReportingDependencies>.Instance, StoreSupport.Factory(failUpload), _meterFactory);
}
