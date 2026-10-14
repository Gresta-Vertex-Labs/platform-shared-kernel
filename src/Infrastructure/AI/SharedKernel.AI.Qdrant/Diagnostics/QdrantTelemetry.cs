using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.AI.Qdrant.Diagnostics;

/// <summary>
/// This package's tracing and metrics: one <see cref="ActivitySource"/> and one <see cref="Meter"/>, both named
/// <see cref="IntelligenceWellKnown.ActivitySourceName"/> / <see cref="IntelligenceWellKnown.MeterName"/> so the host's
/// <c>SharedKernel.*</c> subscription picks them up with no reference to this domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spans</b> are <c>ai {operation}</c>, kind Client, tagged with the bounded configuration values
/// <c>ai.provider</c>, <c>ai.operation</c> and <c>ai.collection</c> only. A record id, vector, filter value, metadata
/// value or key is never a tag. Failures are recorded only through the shared <c>Primitives</c> recorder.
/// </para>
/// <para>
/// <b>Instruments</b> are created on a meter from <see cref="IMeterFactory"/> (two providers asking the same factory for
/// the same name share one meter and stay separable by <c>ai.provider</c>): the duration histogram
/// (<see cref="IntelligenceWellKnown.OperationDurationInstrumentName"/>, unit <c>s</c>) and the record counter
/// (<see cref="IntelligenceWellKnown.VectorRecordsInstrumentName"/>, unit <c>{record}</c>).
/// </para>
/// </remarks>
internal sealed class QdrantTelemetry
{
    /// <summary>The name of this package's <see cref="ActivitySource"/>, the same value for every provider (<see cref="IntelligenceWellKnown.ActivitySourceName"/>).</summary>
    public const string SourceName = IntelligenceWellKnown.ActivitySourceName;

    /// <summary>The name of this package's <see cref="Meter"/>, the same value for every provider (<see cref="IntelligenceWellKnown.MeterName"/>).</summary>
    public const string MeterName = IntelligenceWellKnown.MeterName;

    private static readonly string Version = TelemetryVersion.Of(typeof(QdrantTelemetry).Assembly);

    private readonly Histogram<double> _duration;
    private readonly Counter<long> _records;

    /// <summary>Initialises the telemetry over <paramref name="meterFactory"/>.</summary>
    public QdrantTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(MeterName) { Version = Version });
        _duration = meter.CreateHistogram<double>(
            IntelligenceWellKnown.OperationDurationInstrumentName,
            unit: "s",
            description: "Duration, in seconds, of an AI provider operation.");
        _records = meter.CreateCounter<long>(
            IntelligenceWellKnown.VectorRecordsInstrumentName,
            unit: "{record}",
            description: "Number of vector records a write operation succeeded or failed to store.");
    }

    /// <summary>Gets the shared activity source of this package.</summary>
    internal static ActivitySource Source { get; } = new(SourceName, Version);

    /// <summary>Starts the span and the stopwatch of one operation.</summary>
    /// <param name="operation">An <see cref="IntelligenceOperations"/> value.</param>
    /// <param name="collectionName">The collection name (configuration, not tenant data).</param>
    /// <returns>The scope; dispose it to end the span and record the duration.</returns>
    public QdrantOperationScope Begin(string operation, string collectionName) => new(this, operation, collectionName);

    /// <summary>Counts the records a write operation stored or failed to store.</summary>
    public void RecordRecords(string operation, string collectionName, long succeeded, long failed)
    {
        if (succeeded > 0)
        {
            _records.Add(succeeded, RecordTags(operation, collectionName, WellKnownOutcomes.Success));
        }

        if (failed > 0)
        {
            _records.Add(failed, RecordTags(operation, collectionName, WellKnownOutcomes.Failure));
        }
    }

    internal void RecordDuration(string operation, string collectionName, double seconds, string outcome, string? errorType)
    {
        var tags = new TagList
        {
            { IntelligenceTagKeys.Provider, IntelligenceWellKnown.QdrantProviderName },
            { IntelligenceTagKeys.Operation, operation },
            { IntelligenceTagKeys.Collection, collectionName },
            { WellKnownTagKeys.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorType);
        }

        _duration.Record(seconds, tags);
    }

    private static TagList RecordTags(string operation, string collectionName, string outcome) => new()
    {
        { IntelligenceTagKeys.Provider, IntelligenceWellKnown.QdrantProviderName },
        { IntelligenceTagKeys.Operation, operation },
        { IntelligenceTagKeys.Collection, collectionName },
        { WellKnownTagKeys.Outcome, outcome },
    };
}

/// <summary>
/// One operation's span and stopwatch. Records exactly one duration observation and ends the span when disposed, with
/// the outcome set by <c>Complete</c>/<c>Fail</c> (an enumerator abandoned before either is a success).
/// </summary>
internal sealed class QdrantOperationScope : IDisposable
{
    private readonly QdrantTelemetry _telemetry;
    private readonly string _operation;
    private readonly string _collectionName;
    private readonly long _started;
    private string? _outcome;
    private string? _errorType;
    private bool _disposed;

    internal QdrantOperationScope(QdrantTelemetry telemetry, string operation, string collectionName)
    {
        _telemetry = telemetry;
        _operation = operation;
        _collectionName = collectionName;
        _started = Stopwatch.GetTimestamp();

        Activity = QdrantTelemetry.Source.StartActivity("ai " + operation, ActivityKind.Client);
        if (Activity is { IsAllDataRequested: true } activity)
        {
            activity.SetTag(IntelligenceTagKeys.Provider, IntelligenceWellKnown.QdrantProviderName);
            activity.SetTag(IntelligenceTagKeys.Operation, operation);
            activity.SetTag(IntelligenceTagKeys.Collection, collectionName);
        }
    }

    /// <summary>Gets the span, or <see langword="null"/> when nothing listens.</summary>
    public Activity? Activity { get; }

    /// <summary>Records the outcome of a <see cref="Result{T}"/> on the span and returns it.</summary>
    public Result<T> Complete<T>(Result<T> result)
    {
        _outcome = Activity.RecordResult(result);
        _errorType = result.IsFailure ? result.Error.Type.ToString() : null;
        return result;
    }

    /// <summary>Records the outcome of a <see cref="Result"/> on the span and returns it.</summary>
    public Result Complete(Result result)
    {
        _outcome = Activity.RecordResult(result);
        _errorType = result.IsFailure ? result.Error.Type.ToString() : null;
        return result;
    }

    /// <summary>Records a classified failure that is thrown rather than returned (a stream).</summary>
    public void Fail(SharedKernel.Primitives.Errors.Error error)
    {
        _outcome = Activity.RecordFailure(error);
        _errorType = error.Type.ToString();
    }

    /// <summary>Records an exception: a defect, or the caller's cancellation (which is not an error).</summary>
    public void Fail(Exception exception)
    {
        _outcome = Activity.RecordException(exception);
        _errorType = _outcome == WellKnownOutcomes.Exception ? exception.GetType().FullName : null;
    }

    /// <summary>
    /// Records <paramref name="exception"/> and returns <see langword="false"/>. For use as an exception filter, where a
    /// stream must record an unclassified exception without catching it (the original stack is untouched).
    /// </summary>
    public bool FailAndDecline(Exception exception)
    {
        Fail(exception);
        return false;
    }

    /// <summary>Marks the operation successful.</summary>
    public void Succeed() => _outcome = WellKnownOutcomes.Success;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        _telemetry.RecordDuration(_operation, _collectionName, seconds, _outcome ?? WellKnownOutcomes.Success, _errorType);
        Activity?.Dispose();
    }
}
