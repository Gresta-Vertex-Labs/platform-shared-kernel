using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Telemetry;

/// <summary>The recipe from the README, run against a local source and meter.</summary>
public sealed class TelemetryAcceptanceTests
{
    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    [Fact]
    public async Task ASpanAndAMetric_AreAssertedInAFewLines()
    {
        var unique = SpanFactory.Unique("acceptance");
        using var source = new ActivitySource(unique);
        using var meter = new Meter(unique);
        var publishes = meter.CreateCounter<long>("sharedkernel.messaging.publish.count", unit: "{message}");
        var failure = Error.Unavailable("messaging.unavailable", "Broker unavailable.");

        using var spans = ActivityRecorder.StartRecording(source.Name);
        using var metrics = MetricRecorder.StartRecording(meter.Name);

        await PublishAsync("orders");

        spans.RecordedActivities.ShouldHaveSingleSpan("messaging publish")
            .ShouldHaveKind(ActivityKind.Producer)
            .ShouldHaveTag("messaging.destination", "orders")
            .ShouldBeRecordedFailure(failure);
        metrics.ShouldHaveInstrument("sharedkernel.messaging.publish.count", "{message}", InstrumentKind.Counter)
            .ShouldHaveMeasurement("sharedkernel.messaging.publish.count", 1, Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure))
            .ShouldHaveTagSet("sharedkernel.messaging.publish.count", "messaging.destination", WellKnownTagKeys.Outcome)
            .ShouldUseOutcomeVocabulary("sharedkernel.messaging.publish.count");

        async Task PublishAsync(string destination)
        {
            using var activity = source.StartActivity("messaging publish", ActivityKind.Producer);
            activity?.SetTag("messaging.destination", destination);
            await Task.Yield();
            var outcome = activity.RecordFailure(failure);
            publishes.Add(1, Tag(WellKnownTagKeys.Outcome, outcome), Tag("messaging.destination", destination));
        }
    }

    [Fact]
    public async Task AnExceptionSpan_IsAssertedWithItsEvent()
    {
        var unique = SpanFactory.Unique("acceptance.exception");
        using var source = new ActivitySource(unique);
        using var spans = ActivityRecorder.StartRecording(source.Name);

        using (var activity = source.StartActivity("storage upload", ActivityKind.Client))
        {
            try
            {
                await Task.Yield();
                throw new TimeoutException("socket timed out");
            }
            catch (TimeoutException exception)
            {
                activity.RecordException(exception);
            }
        }

        spans.RecordedActivities.ShouldHaveSingleSpan("storage upload")
            .ShouldBeRecordedException(typeof(TimeoutException))
            .ShouldHaveEvent("exception");
    }
}
