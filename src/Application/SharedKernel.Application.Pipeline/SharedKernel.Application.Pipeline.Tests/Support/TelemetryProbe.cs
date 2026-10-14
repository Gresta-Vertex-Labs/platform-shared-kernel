using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Metrics;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.Support;

/// <summary>
/// Captures the spans, measurements and logs of one test.
/// </summary>
/// <remarks>
/// The <see cref="ApplicationDiagnostics.ActivitySource"/> and the meter name are shared by every test in the
/// assembly and tests run in parallel, so a probe starts a root span of its own: <see cref="Spans"/> returns only the
/// spans of that trace. Measurements are told apart by the unique request type each test declares.
/// </remarks>
internal sealed class TelemetryProbe : IDisposable
{
    private readonly ActivitySource _testSource;

    public TelemetryProbe()
    {
        var sourceName = "SharedKernel.Application.Tests." + Guid.NewGuid().ToString("N");
        _testSource = new ActivitySource(sourceName);
        Activities = ActivityRecorder.StartRecording(ApplicationDiagnostics.ActivitySourceName, sourceName);
        Metrics = MetricRecorder.StartRecording(ApplicationMetrics.MeterName);
        Root = _testSource.StartActivity("test root")
            ?? throw new InvalidOperationException("The test root span was not sampled.");
    }

    public ActivityRecorder Activities { get; }

    public MetricRecorder Metrics { get; }

    public Activity Root { get; }

    /// <summary>The stopped spans of this test's trace, excluding the root.</summary>
    public IReadOnlyList<Activity> Spans =>
        [.. Activities.ForTrace(Root.TraceId).Where(activity => activity != Root)];

    public IReadOnlyList<SharedKernel.Testing.Logging.LogRecord> Records(IServiceProvider provider) =>
        [.. ((InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>()).Loggers.Values.SelectMany(logger => logger.Records)];

    public static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    public void Dispose()
    {
        Root.Stop();
        Activities.Dispose();
        Metrics.Dispose();
        _testSource.Dispose();
    }
}
