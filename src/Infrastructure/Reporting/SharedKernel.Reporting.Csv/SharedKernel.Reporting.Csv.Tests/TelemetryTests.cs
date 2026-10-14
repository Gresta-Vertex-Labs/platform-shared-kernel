using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Reporting.Csv.Tests;

/// <summary>The Csv exporter emits the same span and instruments as every provider (they come from the shared base).</summary>
public sealed class TelemetryTests
{
    [Fact]
    public async Task Export_RecordsTheSharedSpanAndInstruments()
    {
        using var spans = ActivityRecorder.StartRecording(ReportingDiagnostics.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(ReportingDiagnostics.MeterName);
        var exporter = new CsvReportExporter<TestRow>(new ReportingDependencies(NullLogger<ReportingDependencies>.Instance), Options.Create(new CsvExportOptions()));
        ReportDefinition<TestRow> definition = ReportDefinition.For<TestRow>().Column("Name", r => r.Name).Build();
        using var stream = new MemoryStream();

        Result<ReportStreamOutcome> result = await exporter.ExportToStreamAsync(Generate(3), definition, stream);

        result.IsSuccess.Should().BeTrue();
        spans.RecordedActivities.ShouldHaveSingleSpan("reporting export")
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveTag("reporting.format", "csv")
            .ShouldHaveTag("reporting.operation", "export")
            .ShouldHaveTag("reporting.row_count", 3L)
            .ShouldHaveTag("reporting.size_bytes", stream.Length)
            .ShouldBeRecordedSuccess();
        metrics
            .ShouldHaveInstrument("sharedkernel.reporting.operation.duration", "s", InstrumentKind.Histogram)
            .ShouldHaveInstrument("sharedkernel.reporting.rows", "{row}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.reporting.bytes", "By", InstrumentKind.Counter)
            .ShouldHaveSum("sharedkernel.reporting.rows", 3, new KeyValuePair<string, object?>("reporting.format", "csv"))
            .ShouldHaveSum("sharedkernel.reporting.bytes", stream.Length, new KeyValuePair<string, object?>("reporting.format", "csv"))
            .ShouldHaveMeasurement(
                "sharedkernel.reporting.operation.duration",
                null,
                new KeyValuePair<string, object?>("outcome", WellKnownOutcomes.Success));
    }

    private static async IAsyncEnumerable<TestRow> Generate(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            yield return new TestRow(i, $"name-{i}", i * 1.5m, DateTime.UnixEpoch);
        }
    }
}
