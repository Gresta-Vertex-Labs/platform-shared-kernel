using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Search.Abstractions.Constants;

namespace CatalogApi;

/// <summary>
/// An in-process listener on the <c>SharedKernel.Search</c> <see cref="ActivitySource"/> and
/// <see cref="Meter"/>, so <c>GET /diagnostics/telemetry</c> can show what the search packages actually
/// emitted while this service was running.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a sample affordance, not a pattern to copy.</b> A real service exports telemetry through
/// the OpenTelemetry pipeline <c>AddServiceDefaults()</c> already wires; it does not keep spans in
/// memory. It exists here because "is search telemetry actually flowing?" is a question this sample is
/// specifically meant to answer out loud: both provider packages declared an <c>ActivitySource</c> and
/// a <c>Meter</c> and never wrote to either until the pre-publish pass, while
/// <c>WithSearchTelemetry()</c> dutifully subscribed to both. A green dashboard with no data is the
/// failure mode this endpoint makes impossible to miss.
/// </para>
/// <para>
/// It subscribes by the same bare string names <c>13.ServiceDefaults</c> uses, so it observes exactly
/// what a real exporter would.
/// </para>
/// </remarks>
public sealed class TelemetryProbe : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener;
    private readonly ConcurrentQueue<SpanRecord> _spans = new();
    private readonly ConcurrentQueue<MeasurementRecord> _measurements = new();

    public TelemetryProbe()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SearchWellKnown.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _spans.Enqueue(new SpanRecord(
                activity.OperationName,
                activity.GetTagItem(SearchWellKnown.IndexTagName)?.ToString(),
                activity.GetTagItem(SearchWellKnown.OperationTagName)?.ToString(),
                activity.GetTagItem(SearchWellKnown.ProviderTagName)?.ToString(),
                activity.GetTagItem("error.type")?.ToString(),
                Math.Round(activity.Duration.TotalMilliseconds, 2))),
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SearchWellKnown.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _measurements.Enqueue(Record(instrument.Name, value, tags)));
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Enqueue(Record(instrument.Name, value, tags)));
        _meterListener.Start();
    }

    public IReadOnlyList<SpanRecord> Spans => [.. _spans];

    public IReadOnlyList<MeasurementRecord> Measurements => [.. _measurements];

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    private static MeasurementRecord Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        // Copied out of the ref-struct span here: a ReadOnlySpan<T> parameter cannot be captured by a
        // lambda or a local function.
        string? index = null;
        string? operation = null;
        string? provider = null;
        string? errorType = null;
        foreach (var tag in tags)
        {
            switch (tag.Key)
            {
                case SearchWellKnown.IndexTagName:
                    index = tag.Value?.ToString();
                    break;
                case SearchWellKnown.OperationTagName:
                    operation = tag.Value?.ToString();
                    break;
                case SearchWellKnown.ProviderTagName:
                    provider = tag.Value?.ToString();
                    break;
                case "error.type":
                    errorType = tag.Value?.ToString();
                    break;
                default:
                    break;
            }
        }

        return new MeasurementRecord(instrument, value, index, operation, provider, errorType);
    }

    public sealed record SpanRecord(
        string Name, string? Index, string? Operation, string? Provider, string? ErrorType, double DurationMs);

    public sealed record MeasurementRecord(
        string Instrument, double Value, string? Index, string? Operation, string? Provider, string? ErrorType);
}
