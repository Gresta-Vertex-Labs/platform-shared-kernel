using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace SharedKernel.Testing.Telemetry;

/// <summary>The kind of a recorded <see cref="Instrument"/>.</summary>
public enum InstrumentKind
{
    /// <summary>A kind this recorder does not classify.</summary>
    Unknown = 0,

    /// <summary><see cref="Counter{T}"/>.</summary>
    Counter,

    /// <summary><see cref="UpDownCounter{T}"/>.</summary>
    UpDownCounter,

    /// <summary><see cref="Histogram{T}"/>.</summary>
    Histogram,

    /// <summary><see cref="Gauge{T}"/>.</summary>
    Gauge,

    /// <summary><see cref="ObservableCounter{T}"/>.</summary>
    ObservableCounter,

    /// <summary><see cref="ObservableUpDownCounter{T}"/>.</summary>
    ObservableUpDownCounter,

    /// <summary><see cref="ObservableGauge{T}"/>.</summary>
    ObservableGauge,
}

/// <summary>A snapshot of an <see cref="Instrument"/> a <see cref="MetricRecorder"/> saw published.</summary>
/// <param name="MeterName">The owning meter's name.</param>
/// <param name="Name">The instrument name.</param>
/// <param name="Unit">The declared unit, or <see langword="null"/> when none.</param>
/// <param name="Description">The declared description, or <see langword="null"/> when none.</param>
/// <param name="Kind">The instrument kind.</param>
/// <param name="ValueType">The numeric type the instrument measures.</param>
public sealed record RecordedInstrument(
    string MeterName,
    string Name,
    string? Unit,
    string? Description,
    InstrumentKind Kind,
    Type ValueType);

/// <summary>A snapshot of one measurement a <see cref="MetricRecorder"/> received.</summary>
/// <param name="MeterName">The owning meter's name.</param>
/// <param name="InstrumentName">The instrument name.</param>
/// <param name="Value">The value, widened to <see cref="double"/>.</param>
/// <param name="Tags">The tags in the order the producer supplied them.</param>
public sealed record RecordedMeasurement(
    string MeterName,
    string InstrumentName,
    double Value,
    IReadOnlyList<KeyValuePair<string, object?>> Tags)
{
    /// <summary>Reads a tag value by exact key.</summary>
    /// <param name="key">The tag key.</param>
    /// <param name="value">The value when found.</param>
    /// <returns><see langword="true"/> when the tag exists.</returns>
    public bool TryGetTag(string key, out object? value)
    {
        foreach (var tag in Tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                value = tag.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    internal bool HasTags(IEnumerable<KeyValuePair<string, object?>> expected)
    {
        foreach (var tag in expected)
        {
            if (!TryGetTag(tag.Key, out var actual) || !Equals(actual, tag.Value))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Records the instruments and measurements of the <see cref="Meter"/>s a test names, so the test can assert on
/// metrics after the code under test has run.
/// </summary>
/// <remarks>
/// <para>
/// One recorder is one <see cref="MeterListener"/> that enables every instrument of the matched meters (exact names
/// or a prefix) and handles <c>byte</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c>, <c>double</c> and
/// <c>decimal</c> measurements. Reads are snapshots, <see cref="Dispose"/> is idempotent, snapshots stay readable
/// afterwards and nothing is recorded after it.
/// </para>
/// <para>
/// <b>Static meters are shared.</b> A static <see cref="Meter"/> also receives the measurements of tests running in
/// parallel. Filter by tags (<see cref="Sum"/>, <see cref="Count"/>, <c>MetricAssertions</c>) or keep the test
/// collection serial; a <see cref="Meter"/> created through a per-test <see cref="IMeterFactory"/> is isolated.
/// </para>
/// </remarks>
public sealed class MetricRecorder : IDisposable
{
    private readonly ConcurrentQueue<RecordedInstrument> _instruments = new();
    private readonly ConcurrentQueue<RecordedMeasurement> _measurements = new();
    private readonly MeterListener _listener = new();
    private volatile bool _disposed;

    private MetricRecorder(Func<string, bool> meterFilter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (_disposed || !meterFilter(instrument.Meter.Name))
            {
                return;
            }

            _instruments.Enqueue(Describe(instrument));
            listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<byte>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<short>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<float>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<decimal>((i, v, t, _) => Record(i, (double)v, t));
        _listener.Start();
    }

    /// <summary>Every instrument of the matched meters seen so far, including ones that never measured.</summary>
    public IReadOnlyList<RecordedInstrument> Instruments => [.. _instruments];

    /// <summary>Every measurement received so far, in arrival order.</summary>
    public IReadOnlyList<RecordedMeasurement> Measurements => [.. _measurements];

    /// <summary>Records every instrument of the meters named exactly <paramref name="meterNames"/>.</summary>
    /// <param name="meterNames">Exact <see cref="Meter.Name"/> values.</param>
    /// <returns>A disposable recorder; dispose it to stop listening.</returns>
    public static MetricRecorder StartRecording(params string[] meterNames)
    {
        ArgumentNullException.ThrowIfNull(meterNames);
        if (meterNames.Length == 0)
        {
            throw new ArgumentException("At least one meter name is required.", nameof(meterNames));
        }

        var names = new HashSet<string>(meterNames, StringComparer.Ordinal);
        return new MetricRecorder(names.Contains);
    }

    /// <summary>Records every instrument of every meter whose name starts with <paramref name="meterNamePrefix"/>.</summary>
    /// <param name="meterNamePrefix">The ordinal name prefix, for example <c>"SharedKernel."</c>.</param>
    /// <returns>A disposable recorder; dispose it to stop listening.</returns>
    public static MetricRecorder StartRecordingPrefix(string meterNamePrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(meterNamePrefix);
        return new MetricRecorder(name => name.StartsWith(meterNamePrefix, StringComparison.Ordinal));
    }

    /// <summary>The measurements of one instrument, in arrival order.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>A snapshot.</returns>
    public IReadOnlyList<RecordedMeasurement> MeasurementsOf(string instrument) =>
        [.. _measurements.Where(m => m.InstrumentName == instrument)];

    /// <summary>Asks every observable instrument (counter, up-down counter, gauge) to report now.</summary>
    public void Collect()
    {
        if (!_disposed)
        {
            _listener.RecordObservableInstruments();
        }
    }

    /// <summary>Forgets every recorded measurement; instruments and recording continue.</summary>
    public void Clear() => _measurements.Clear();

    /// <summary>The sum of the <paramref name="instrument"/> measurements that carry every tag in <paramref name="tags"/>.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="tags">A tag subset the measurement must contain; empty for all measurements.</param>
    /// <returns>The sum, <c>0</c> when nothing matches.</returns>
    public double Sum(string instrument, params KeyValuePair<string, object?>[] tags) =>
        Matching(instrument, tags).Sum(m => m.Value);

    /// <summary>The number of <paramref name="instrument"/> measurements that carry every tag in <paramref name="tags"/>.</summary>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="tags">A tag subset the measurement must contain; empty for all measurements.</param>
    /// <returns>The number of matching measurements.</returns>
    public int Count(string instrument, params KeyValuePair<string, object?>[] tags) =>
        Matching(instrument, tags).Count();

    /// <summary>Stops listening. Idempotent; snapshots taken earlier stay readable.</summary>
    public void Dispose()
    {
        _disposed = true;
        _listener.Dispose();
    }

    internal IEnumerable<RecordedMeasurement> Matching(string instrument, KeyValuePair<string, object?>[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return _measurements.Where(m => m.InstrumentName == instrument && m.HasTags(tags));
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (_disposed)
        {
            return;
        }

        _measurements.Enqueue(new RecordedMeasurement(instrument.Meter.Name, instrument.Name, value, tags.ToArray()));
    }

    private static RecordedInstrument Describe(Instrument instrument)
    {
        var type = instrument.GetType();
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        var kind = definition switch
        {
            _ when definition == typeof(Counter<>) => InstrumentKind.Counter,
            _ when definition == typeof(UpDownCounter<>) => InstrumentKind.UpDownCounter,
            _ when definition == typeof(Histogram<>) => InstrumentKind.Histogram,
            _ when definition == typeof(Gauge<>) => InstrumentKind.Gauge,
            _ when definition == typeof(ObservableCounter<>) => InstrumentKind.ObservableCounter,
            _ when definition == typeof(ObservableUpDownCounter<>) => InstrumentKind.ObservableUpDownCounter,
            _ when definition == typeof(ObservableGauge<>) => InstrumentKind.ObservableGauge,
            _ => InstrumentKind.Unknown,
        };

        var valueType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
        return new RecordedInstrument(
            instrument.Meter.Name,
            instrument.Name,
            instrument.Unit,
            instrument.Description,
            kind,
            valueType);
    }
}
