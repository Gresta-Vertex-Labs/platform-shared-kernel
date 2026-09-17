using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Caching.FusionCache.Tests.Telemetry;

/// <summary>One measurement recorded against the <c>SharedKernel.Caching</c> meter, with its tags.</summary>
internal sealed record RecordedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)
{
    public string? Tag(string name) => Tags.TryGetValue(name, out var value) ? value?.ToString() : null;
}

/// <summary>
/// Captures every measurement of the <c>SharedKernel.Caching</c> meter while alive. The callbacks run on
/// whatever thread records (FusionCache raises hit events in the background), so the store is concurrent.
/// </summary>
internal sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();

    public MetricRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "SharedKernel.Caching")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Enqueue(new RecordedMeasurement(instrument.Name, value, ToDictionary(tags))));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            Measurements.Enqueue(new RecordedMeasurement(instrument.Name, value, ToDictionary(tags))));
        _listener.Start();
    }

    public ConcurrentQueue<RecordedMeasurement> Measurements { get; } = new();

    public double Sum(string instrument, string keyPrefix, string? level = null) =>
        Measurements
            .Where(m => m.Instrument == instrument
                && m.Tag("cache.key_prefix") == keyPrefix
                && (level is null || m.Tag("cache.level") == level))
            .Sum(m => m.Value);

    public void Dispose() => _listener.Dispose();

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            result[tag.Key] = tag.Value;
        return result;
    }
}

/// <summary>Captures stopped activities of the <c>SharedKernel.Caching</c> source while alive.</summary>
internal sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;

    public ActivityRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Caching",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public ConcurrentQueue<Activity> Activities { get; } = new();

    public void Dispose() => _listener.Dispose();
}

internal sealed record CapturedLog(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    Exception? Exception);

/// <summary>An <see cref="ILoggerProvider"/> that keeps every log entry, at every level.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Logs);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Enqueue(new CapturedLog(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                exception));
    }
}

internal static class Eventually
{
    /// <summary>Polls <paramref name="condition"/> until it holds or five seconds pass.</summary>
    public static async Task<bool> HoldsAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(20);
        }

        return condition();
    }
}
