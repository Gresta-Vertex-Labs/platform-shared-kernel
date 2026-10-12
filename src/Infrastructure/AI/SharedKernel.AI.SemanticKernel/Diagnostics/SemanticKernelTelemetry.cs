using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.AI.SemanticKernel.Diagnostics;

/// <summary>
/// This package's tracing and metrics: one <see cref="ActivitySource"/> and one <see cref="Meter"/>, both named
/// <see cref="IntelligenceWellKnown.ActivitySourceName"/> / <see cref="IntelligenceWellKnown.MeterName"/> so the host's
/// <c>SharedKernel.*</c> subscription picks them up with no reference to this domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spans</b> are <c>ai {operation}</c>, kind Client, tagged with the bounded configuration values
/// <c>ai.provider</c>, <c>ai.operation</c> and <c>ai.model</c> only. Prompt text, completion text, message content, tool
/// arguments and the API key are never tags or log values. <c>ai.model</c> is the configured model id, or
/// <see cref="IntelligenceWellKnown.OtherModel"/> when the caller asked for a different one, so a caller cannot mint
/// unbounded dimensions.
/// </para>
/// <para>
/// <b>Instruments</b> are created on a meter from <see cref="IMeterFactory"/>: the duration histogram
/// (<see cref="IntelligenceWellKnown.OperationDurationInstrumentName"/>, unit <c>s</c>) and the token counter
/// (<see cref="IntelligenceWellKnown.TokensInstrumentName"/>, unit <c>{token}</c>). Tokens are counted once per completed
/// call from the usage the provider actually returned; a failed call counts nothing, and a stream counts the usage its
/// final chunk carried (OpenAI-compatible endpoints report cumulative usage once, so nothing is double counted).
/// </para>
/// </remarks>
internal sealed class SemanticKernelTelemetry
{
    private static readonly string Version = TelemetryVersion.Of(typeof(SemanticKernelTelemetry).Assembly);

    private readonly Histogram<double> _duration;
    private readonly Counter<long> _tokens;

    /// <summary>Initialises the telemetry over <paramref name="meterFactory"/>.</summary>
    public SemanticKernelTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(IntelligenceWellKnown.MeterName) { Version = Version });
        _duration = meter.CreateHistogram<double>(
            IntelligenceWellKnown.OperationDurationInstrumentName,
            unit: "s",
            description: "Duration, in seconds, of an AI provider operation.");
        _tokens = meter.CreateCounter<long>(
            IntelligenceWellKnown.TokensInstrumentName,
            unit: "{token}",
            description: "Number of tokens an AI provider billed, by direction.");
    }

    /// <summary>Gets the shared activity source of this package.</summary>
    internal static ActivitySource Source { get; } = new(IntelligenceWellKnown.ActivitySourceName, Version);

    /// <summary>Returns the bounded <c>ai.model</c> value: <paramref name="configuredModelId"/> unless the caller asked for another model.</summary>
    public static string BoundedModel(string? requestedModelId, string configuredModelId) =>
        requestedModelId is null || string.Equals(requestedModelId, configuredModelId, StringComparison.Ordinal)
            ? configuredModelId
            : IntelligenceWellKnown.OtherModel;

    /// <summary>Starts the span and the stopwatch of one operation.</summary>
    /// <param name="operation">An <see cref="IntelligenceOperations"/> value.</param>
    /// <param name="boundedModel">The <see cref="BoundedModel"/> value.</param>
    /// <returns>The scope; dispose it to end the span and record the duration.</returns>
    public SemanticKernelOperationScope Begin(string operation, string boundedModel) => new(this, operation, boundedModel);

    /// <summary>Counts the tokens the provider reported, once per completed call. Zero is not recorded.</summary>
    public void RecordTokens(string operation, string boundedModel, TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (usage.PromptTokens > 0)
        {
            _tokens.Add(usage.PromptTokens, TokenTags(operation, boundedModel, IntelligenceWellKnown.TokenTypeInput));
        }

        if (usage.CompletionTokens > 0)
        {
            _tokens.Add(usage.CompletionTokens, TokenTags(operation, boundedModel, IntelligenceWellKnown.TokenTypeOutput));
        }
    }

    internal void RecordDuration(string operation, string boundedModel, double seconds, string outcome, string? errorType)
    {
        var tags = new TagList
        {
            { IntelligenceWellKnown.ProviderTagName, IntelligenceWellKnown.SemanticKernelProviderName },
            { IntelligenceWellKnown.OperationTagName, operation },
            { IntelligenceWellKnown.ModelTagName, boundedModel },
            { WellKnownTagKeys.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorType);
        }

        _duration.Record(seconds, tags);
    }

    private static TagList TokenTags(string operation, string boundedModel, string tokenType) => new()
    {
        { IntelligenceWellKnown.ProviderTagName, IntelligenceWellKnown.SemanticKernelProviderName },
        { IntelligenceWellKnown.OperationTagName, operation },
        { IntelligenceWellKnown.ModelTagName, boundedModel },
        { IntelligenceWellKnown.TokenTypeTagName, tokenType },
    };
}

/// <summary>
/// One operation's span and stopwatch. Records exactly one duration observation and ends the span when disposed, with
/// the outcome set by <c>Complete</c>/<c>Fail</c> (an enumerator abandoned before either is a success).
/// </summary>
internal sealed class SemanticKernelOperationScope : IDisposable
{
    private readonly SemanticKernelTelemetry _telemetry;
    private readonly string _operation;
    private readonly string _boundedModel;
    private readonly long _started;
    private string? _outcome;
    private string? _errorType;
    private bool _disposed;

    internal SemanticKernelOperationScope(SemanticKernelTelemetry telemetry, string operation, string boundedModel)
    {
        _telemetry = telemetry;
        _operation = operation;
        _boundedModel = boundedModel;
        _started = Stopwatch.GetTimestamp();

        Activity = SemanticKernelTelemetry.Source.StartActivity("ai " + operation, ActivityKind.Client);
        if (Activity is { IsAllDataRequested: true } activity)
        {
            activity.SetTag(IntelligenceWellKnown.ProviderTagName, IntelligenceWellKnown.SemanticKernelProviderName);
            activity.SetTag(IntelligenceWellKnown.OperationTagName, operation);
            activity.SetTag(IntelligenceWellKnown.ModelTagName, boundedModel);
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
        _telemetry.RecordDuration(_operation, _boundedModel, seconds, _outcome ?? WellKnownOutcomes.Success, _errorType);
        Activity?.Dispose();
    }
}
