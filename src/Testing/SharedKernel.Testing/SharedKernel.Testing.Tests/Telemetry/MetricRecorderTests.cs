using System.Diagnostics.Metrics;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Telemetry;

public sealed class MetricRecorderTests
{
    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    [Fact]
    public void Counter_RecordsValuesAndTags_InArrivalOrder()
    {
        using var meter = new Meter(Unique("counter"));
        var counter = meter.CreateCounter<int>("sharedkernel.selftest.requests", unit: "{request}", description: "Requests.");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        counter.Add(2, Tag("route", "a"), Tag("outcome", "success"));
        counter.Add(3, Tag("route", "b"));

        var instrument = Assert.Single(recorder.Instruments);
        Assert.Equal(meter.Name, instrument.MeterName);
        Assert.Equal("sharedkernel.selftest.requests", instrument.Name);
        Assert.Equal("{request}", instrument.Unit);
        Assert.Equal("Requests.", instrument.Description);
        Assert.Equal(InstrumentKind.Counter, instrument.Kind);
        Assert.Equal(typeof(int), instrument.ValueType);

        Assert.Equal(2, recorder.Measurements.Count);
        Assert.Equal(2d, recorder.Measurements[0].Value);
        Assert.Equal(["route", "outcome"], recorder.Measurements[0].Tags.Select(t => t.Key));
        Assert.Equal(5d, recorder.Sum("sharedkernel.selftest.requests"));
        Assert.Equal(2d, recorder.Sum("sharedkernel.selftest.requests", Tag("route", "a")));
        Assert.Equal(1, recorder.Count("sharedkernel.selftest.requests", Tag("route", "b")));
        Assert.Equal(0, recorder.Count("sharedkernel.selftest.requests", Tag("route", "c")));
        Assert.Equal(2, recorder.MeasurementsOf("sharedkernel.selftest.requests").Count);
        Assert.Empty(recorder.MeasurementsOf("missing"));
    }

    [Fact]
    public void EveryInstrumentKind_IsClassified_AndMeasuresItsOwnValues()
    {
        using var meter = new Meter(Unique("kinds"));
        var upDown = meter.CreateUpDownCounter<long>("sharedkernel.selftest.updown");
        var histogram = meter.CreateHistogram<double>("sharedkernel.selftest.duration", unit: "s");
        var gauge = meter.CreateGauge<double>("sharedkernel.selftest.gauge");
        var observableValue = 7;
        _ = meter.CreateObservableGauge("sharedkernel.selftest.observable", () => observableValue);
        _ = meter.CreateObservableCounter("sharedkernel.selftest.observed.total", () => 11L);
        _ = meter.CreateObservableUpDownCounter("sharedkernel.selftest.observed.level", () => 13);
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        upDown.Add(5);
        upDown.Add(-2);
        histogram.Record(0.25);
        gauge.Record(1.5);
        Assert.Empty(recorder.MeasurementsOf("sharedkernel.selftest.observable"));
        recorder.Collect();

        var kinds = recorder.Instruments.ToDictionary(i => i.Name, i => i.Kind);
        Assert.Equal(InstrumentKind.UpDownCounter, kinds["sharedkernel.selftest.updown"]);
        Assert.Equal(InstrumentKind.Histogram, kinds["sharedkernel.selftest.duration"]);
        Assert.Equal(InstrumentKind.Gauge, kinds["sharedkernel.selftest.gauge"]);
        Assert.Equal(InstrumentKind.ObservableGauge, kinds["sharedkernel.selftest.observable"]);
        Assert.Equal(InstrumentKind.ObservableCounter, kinds["sharedkernel.selftest.observed.total"]);
        Assert.Equal(InstrumentKind.ObservableUpDownCounter, kinds["sharedkernel.selftest.observed.level"]);

        Assert.Equal(3d, recorder.Sum("sharedkernel.selftest.updown"));
        Assert.Equal(0.25, recorder.Sum("sharedkernel.selftest.duration"));
        Assert.Equal(1.5, recorder.Sum("sharedkernel.selftest.gauge"));
        Assert.Equal(7d, recorder.Sum("sharedkernel.selftest.observable"));
        Assert.Equal(11d, recorder.Sum("sharedkernel.selftest.observed.total"));
        Assert.Equal(13d, recorder.Sum("sharedkernel.selftest.observed.level"));
    }

    [Fact]
    public void AllSevenNumericValueTypes_AreRecorded_AsDouble()
    {
        using var meter = new Meter(Unique("numeric"));
        var bytes = meter.CreateCounter<byte>("sharedkernel.selftest.byte");
        var shorts = meter.CreateCounter<short>("sharedkernel.selftest.short");
        var ints = meter.CreateCounter<int>("sharedkernel.selftest.int");
        var longs = meter.CreateCounter<long>("sharedkernel.selftest.long");
        var floats = meter.CreateCounter<float>("sharedkernel.selftest.float");
        var doubles = meter.CreateCounter<double>("sharedkernel.selftest.double");
        var decimals = meter.CreateCounter<decimal>("sharedkernel.selftest.decimal");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        bytes.Add(1);
        shorts.Add(2);
        ints.Add(3);
        longs.Add(4);
        floats.Add(5.5f);
        doubles.Add(6.25);
        decimals.Add(7.75m);

        Assert.Equal([1d, 2d, 3d, 4d, 5.5d, 6.25d, 7.75d], recorder.Measurements.Select(m => m.Value));
        Assert.Equal(
            [typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
            recorder.Instruments.Select(i => i.ValueType));
    }

    [Fact]
    public void Tags_KeepOrder_AndNullValues_AndInstrumentsWithoutUnitAreReported()
    {
        using var meter = new Meter(Unique("tags"));
        var withUnit = meter.CreateCounter<int>("sharedkernel.selftest.with_unit", unit: "{op}");
        var withoutUnit = meter.CreateCounter<int>("sharedkernel.selftest.without_unit");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        withUnit.Add(1, Tag("z", "last"), Tag("a", null), Tag("m", 3));
        _ = withoutUnit;

        var measurement = Assert.Single(recorder.Measurements);
        Assert.Equal(["z", "a", "m"], measurement.Tags.Select(t => t.Key));
        Assert.True(measurement.TryGetTag("a", out var nullValue));
        Assert.Null(nullValue);
        Assert.Equal(3, measurement.Tags[2].Value);
        Assert.False(measurement.TryGetTag("missing", out _));
        Assert.Equal(1, recorder.Count("sharedkernel.selftest.with_unit", Tag("a", null)));

        var instruments = recorder.Instruments.ToDictionary(i => i.Name);
        Assert.Equal("{op}", instruments["sharedkernel.selftest.with_unit"].Unit);
        Assert.Null(instruments["sharedkernel.selftest.without_unit"].Unit);
        Assert.Empty(recorder.MeasurementsOf("sharedkernel.selftest.without_unit"));
    }

    [Fact]
    public void StartRecordingPrefix_AndSeveralNames_MatchOnlyTheirMeters()
    {
        var prefix = Unique("scope") + ".";
        using var first = new Meter(prefix + "one");
        using var second = new Meter(prefix + "two");
        using var other = new Meter(Unique("elsewhere"));
        var a = first.CreateCounter<int>("sharedkernel.selftest.a");
        var b = second.CreateCounter<int>("sharedkernel.selftest.b");
        var c = other.CreateCounter<int>("sharedkernel.selftest.c");
        using var byPrefix = MetricRecorder.StartRecordingPrefix(prefix);
        using var byNames = MetricRecorder.StartRecording(first.Name, other.Name);

        a.Add(1);
        b.Add(1);
        c.Add(1);

        Assert.Equal(["sharedkernel.selftest.a", "sharedkernel.selftest.b"], byPrefix.Measurements.Select(m => m.InstrumentName));
        Assert.Equal(["sharedkernel.selftest.a", "sharedkernel.selftest.c"], byNames.Measurements.Select(m => m.InstrumentName));
        Assert.Throws<ArgumentException>(() => MetricRecorder.StartRecording(Array.Empty<string>()));
    }

    [Fact]
    public void Clear_ForgetsMeasurements_ButKeepsInstrumentsAndRecording()
    {
        using var meter = new Meter(Unique("clear"));
        var counter = meter.CreateCounter<int>("sharedkernel.selftest.clear");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        counter.Add(1);
        recorder.Clear();
        counter.Add(4);

        Assert.Single(recorder.Instruments);
        Assert.Equal(4d, recorder.Sum("sharedkernel.selftest.clear"));
    }

    [Fact]
    public void Dispose_StopsRecording_KeepsSnapshots_IsIdempotent_AndLeavesASecondRecorderUnaffected()
    {
        using var meter = new Meter(Unique("dispose"));
        var counter = meter.CreateCounter<int>("sharedkernel.selftest.dispose");
        var first = MetricRecorder.StartRecording(meter.Name);
        using var second = MetricRecorder.StartRecording(meter.Name);

        counter.Add(1, Tag("k", "v"));
        var snapshot = first.Measurements;
        first.Dispose();
        first.Dispose();
        counter.Add(2, Tag("k", "v"));
        first.Collect();

        Assert.Single(snapshot);
        Assert.Equal("v", snapshot[0].Tags[0].Value);
        Assert.Single(first.Measurements);
        Assert.Equal(1d, first.Sum("sharedkernel.selftest.dispose"));
        Assert.Equal(3d, second.Sum("sharedkernel.selftest.dispose"));
    }

    [Fact]
    public async Task ConcurrentMeasurements_AreAllRecorded_WithExactPerTagSums()
    {
        using var meter = new Meter(Unique("concurrent"));
        var counter = meter.CreateCounter<long>("sharedkernel.selftest.parallel");
        var histogram = meter.CreateHistogram<int>("sharedkernel.selftest.parallel_size");
        using var recorder = MetricRecorder.StartRecording(meter.Name);

        const int iterations = 3000;
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                _ = recorder.Measurements.Count;
                _ = recorder.Sum("sharedkernel.selftest.parallel");
                _ = recorder.Instruments.Count;
            }
        });

        Parallel.For(0, iterations, i =>
        {
            var tag = Tag("bucket", i % 3);
            counter.Add(2, tag);
            histogram.Record(1, tag);
        });
        await reader;

        Assert.Equal(iterations * 2, recorder.Count("sharedkernel.selftest.parallel") * 2);
        Assert.Equal(iterations * 2d, recorder.Sum("sharedkernel.selftest.parallel"));
        Assert.Equal(1000 * 2d, recorder.Sum("sharedkernel.selftest.parallel", Tag("bucket", 0)));
        Assert.Equal(1000 * 2d, recorder.Sum("sharedkernel.selftest.parallel", Tag("bucket", 2)));
        Assert.Equal(iterations, recorder.Count("sharedkernel.selftest.parallel_size"));
    }

    private static string Unique(string prefix) => SpanFactory.Unique(prefix);
}
