using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Telemetry;

public sealed class MetricAssertionsTests
{
    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    private static InvalidOperationException Fails(Func<object> assertion) => Assert.Throws<InvalidOperationException>(assertion);

    [Fact]
    public void ShouldHaveInstrument_ChecksNameUnitAndKind()
    {
        using var meter = new Meter(SpanFactory.Unique("instrument"));
        meter.CreateCounter<int>("sharedkernel.selftest.sent", unit: "{message}");
        meter.CreateHistogram<double>("sharedkernel.selftest.duration", unit: "ms");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        recorder.ShouldHaveInstrument("sharedkernel.selftest.sent")
            .ShouldHaveInstrument("sharedkernel.selftest.sent", "{message}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.selftest.duration", kind: InstrumentKind.Histogram);

        Assert.Contains("'sharedkernel.selftest.sent'", Fails(() => recorder.ShouldHaveInstrument("missing")).Message);
        Assert.Contains("unit should be 's', but it was 'ms'", Fails(() => recorder.ShouldHaveInstrument("sharedkernel.selftest.duration", "s")).Message);
        Assert.Contains("kind should be 'Histogram'", Fails(() => recorder.ShouldHaveInstrument("sharedkernel.selftest.sent", kind: InstrumentKind.Histogram)).Message);
    }

    [Fact]
    public void MeasurementTagSumAndSet_PassAndFail()
    {
        using var meter = new Meter(SpanFactory.Unique("measurement"));
        var counter = meter.CreateCounter<int>("sharedkernel.selftest.publish", unit: "{message}");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        counter.Add(1, Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure), Tag("destination", "orders"));
        counter.Add(2, Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success), Tag("destination", "orders"));

        recorder.ShouldHaveMeasurement("sharedkernel.selftest.publish")
            .ShouldHaveMeasurement("sharedkernel.selftest.publish", 1, Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure))
            .ShouldHaveMeasurement("sharedkernel.selftest.publish", tags: Tag("destination", "orders"))
            .ShouldHaveTagSet("sharedkernel.selftest.publish", "destination", WellKnownTagKeys.Outcome)
            .ShouldNotHaveTagKey("sharedkernel.selftest.publish", "tenant")
            .ShouldHaveSum("sharedkernel.selftest.publish", 3)
            .ShouldHaveSum("sharedkernel.selftest.publish", 2, Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success));

        var noMatch = Fails(() => recorder.ShouldHaveMeasurement("sharedkernel.selftest.publish", 5));
        Assert.Contains("= 5", noMatch.Message);
        Assert.Contains("{outcome=failure, destination=orders}", noMatch.Message);
        Assert.Contains("no measurements", Fails(() => recorder.ShouldHaveMeasurement("missing", 1)).Message);
        Assert.Contains("exactly the tag keys [destination]", Fails(() => recorder.ShouldHaveTagSet("sharedkernel.selftest.publish", "destination")).Message);
        Assert.Contains("none were recorded", Fails(() => recorder.ShouldHaveTagSet("missing", "a")).Message);
        Assert.Contains("carry the tag 'destination', but 2 did", Fails(() => recorder.ShouldNotHaveTagKey("sharedkernel.selftest.publish", "destination")).Message);
        Assert.Contains("to be 4, but it was 3", Fails(() => recorder.ShouldHaveSum("sharedkernel.selftest.publish", 4)).Message);
    }

    [Fact]
    public void ShouldUseOutcomeVocabulary_AcceptsTheVocabulary_AndRejectsStrangersAndAbsence()
    {
        using var meter = new Meter(SpanFactory.Unique("outcome"));
        var good = meter.CreateCounter<int>("sharedkernel.selftest.good");
        var bad = meter.CreateCounter<int>("sharedkernel.selftest.bad");
        var untagged = meter.CreateCounter<int>("sharedkernel.selftest.untagged");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        foreach (var outcome in new[] { WellKnownOutcomes.Success, WellKnownOutcomes.Failure, WellKnownOutcomes.Exception, WellKnownOutcomes.Canceled })
        {
            good.Add(1, Tag(WellKnownTagKeys.Outcome, outcome));
        }

        bad.Add(1, Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success));
        bad.Add(1, Tag(WellKnownTagKeys.Outcome, "failed"));
        untagged.Add(1);

        recorder.ShouldUseOutcomeVocabulary("sharedkernel.selftest.good");
        Assert.Contains("'failed'", Fails(() => recorder.ShouldUseOutcomeVocabulary("sharedkernel.selftest.bad")).Message);
        Assert.Contains("none did", Fails(() => recorder.ShouldUseOutcomeVocabulary("sharedkernel.selftest.untagged")).Message);
    }

    [Fact]
    public void ShouldNotLeak_ScansEveryTagValue()
    {
        using var meter = new Meter(SpanFactory.Unique("leak"));
        var counter = meter.CreateCounter<int>("sharedkernel.selftest.leak");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        counter.Add(1, Tag("route", "orders"));
        recorder.ShouldNotLeak("tenant-42");

        counter.Add(1, Tag("who", "user tenant-42"));
        Assert.Contains("'sharedkernel.selftest.leak' tag 'who' contains 'tenant-42'", Fails(() => recorder.ShouldNotLeak("tenant-42")).Message);
        Assert.Throws<ArgumentException>(() => recorder.ShouldNotLeak(""));
    }

    [Fact]
    public void ShouldFollowMetricConvention_AcceptsTheConventionalMeter()
    {
        using var meter = new Meter("SharedKernel.SelfTestConvention");
        meter.CreateCounter<long>("sharedkernel.selftestconvention.publish.count", unit: "{message}");
        var duration = meter.CreateHistogram<double>("sharedkernel.selftestconvention.publish.duration", unit: "s");
        using var recorder = MetricRecorder.StartRecording(meter.Name);
        duration.Record(0.1, Tag("messaging.destination", "orders"));

        recorder.ShouldFollowMetricConvention();
    }

    [Fact]
    public void ShouldFollowMetricConvention_ListsEveryViolation()
    {
        using var meter = new Meter(SpanFactory.Unique("Bad meter"));
        meter.CreateCounter<int>("Publish.Count");
        meter.CreateHistogram<double>("sharedkernel.selftest.publish.duration", unit: "ms");
        var tagged = meter.CreateCounter<int>("sharedkernel.selftest.tagged", unit: "{x}");
        using var recorder = MetricRecorder.StartRecording(meter.Name);
        tagged.Add(1, Tag("Bad-Key", 1));

        var message = Fails(() => recorder.ShouldFollowMetricConvention()).Message;

        Assert.Contains("meter '", message);
        Assert.Contains("instrument 'Publish.Count' is not a lowercase", message);
        Assert.Contains("instrument 'Publish.Count' declares no unit", message);
        Assert.Contains("duration histogram 'sharedkernel.selftest.publish.duration' uses unit 'ms' instead of 's'", message);
        Assert.Contains("tag key 'Bad-Key'", message);
    }
}
