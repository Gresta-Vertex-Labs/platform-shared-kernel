using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Telemetry;

namespace SharedKernel.Search.Meilisearch.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Search</c> activity source and meter of the Meilisearch provider. The host's
/// <c>SharedKernel.*</c> subscription (<c>AddServiceDefaults</c>) picks both up; nothing references this type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spans</b> are <c>search {operation}</c>, kind Client (kind Internal for the token issuance, which does no I/O),
/// tagged with the index, the contract operation and the provider. Failures are recorded only through the shared
/// <c>Primitives</c> recorder: a <c>Result</c> failure carries <c>error.code</c> and <c>error.type</c> (the
/// <c>ErrorType</c> name), an unexpected exception carries its full exception event, and a caller's cancellation is
/// never an error and still records its duration with the <c>canceled</c> outcome.
/// </para>
/// <para>
/// <b>Instruments</b> are created on the meter <see cref="IMeterFactory"/> returns for the name, so the ElasticSearch
/// provider's measurements land on the same meter and are told apart by the <c>search.provider</c> tag. Query text,
/// filter values, document ids and cursor tokens are never a tag: they are user data.
/// </para>
/// </remarks>
internal sealed class SearchTelemetry
{
    /// <summary>The source and meter name, declared here as a public const of this assembly (equal to <see cref="SearchWellKnown.ActivitySourceName"/>).</summary>
    public const string Name = SearchWellKnown.ActivitySourceName;

    private static readonly string Version = TelemetryVersion.Of(typeof(SearchTelemetry).Assembly);

    private readonly Histogram<double> _duration;
    private readonly Counter<long> _documents;

    /// <summary>Initialises the telemetry over <paramref name="meterFactory"/>.</summary>
    public SearchTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(new MeterOptions(SearchWellKnown.MeterName) { Version = Version });
        _duration = meter.CreateHistogram<double>(
            SearchInstrumentNames.OperationDuration,
            unit: "s",
            description: "Duration of search operations, by index, operation, provider and outcome.");
        _documents = meter.CreateCounter<long>(
            SearchInstrumentNames.Documents,
            unit: "{document}",
            description: "Documents a successful write or delete was asked to affect, by index, operation and provider.");
    }

    /// <summary>Gets the activity source shared by every provider operation.</summary>
    internal static ActivitySource Source { get; } = new(Name, Version);

    /// <summary>
    /// Starts the span and the stopwatch of one operation. The returned scope records the span outcome and the duration
    /// when one of its completion members is called, and ends the span when disposed.
    /// </summary>
    public SearchOperationScope Start(string operation, string indexName, ActivityKind kind = ActivityKind.Client)
    {
        Activity? activity = Source.StartActivity($"search {operation}", kind);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(SearchTagKeys.Index, indexName);
            activity.SetTag(SearchTagKeys.Operation, operation);
            activity.SetTag(SearchTagKeys.Provider, SearchWellKnown.MeilisearchProviderName);
        }

        return new SearchOperationScope(this, activity, operation, indexName, Stopwatch.GetTimestamp());
    }

    /// <summary>Counts the documents a successful write or delete was asked to affect.</summary>
    public void RecordDocuments(string operation, string indexName, long count)
    {
        if (count <= 0)
        {
            return;
        }

        _documents.Add(
            count,
            new TagList
            {
                { SearchTagKeys.Index, indexName },
                { SearchTagKeys.Operation, operation },
                { SearchTagKeys.Provider, SearchWellKnown.MeilisearchProviderName },
            });
    }

    private void RecordDuration(
        string operation, string indexName, long startTimestamp, string outcome, string? errorType)
    {
        var tags = new TagList
        {
            { SearchTagKeys.Index, indexName },
            { SearchTagKeys.Operation, operation },
            { SearchTagKeys.Provider, SearchWellKnown.MeilisearchProviderName },
            { WellKnownTagKeys.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorType);
        }

        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    /// <summary>One operation's span and stopwatch; records its outcome once.</summary>
    internal sealed class SearchOperationScope : IDisposable
    {
        private readonly SearchTelemetry _owner;
        private readonly string _operation;
        private readonly string _indexName;
        private readonly long _startTimestamp;
        private bool _completed;

        internal SearchOperationScope(
            SearchTelemetry owner, Activity? activity, string operation, string indexName, long startTimestamp)
        {
            _owner = owner;
            Activity = activity;
            _operation = operation;
            _indexName = indexName;
            _startTimestamp = startTimestamp;
        }

        /// <summary>Gets the span, or <see langword="null"/> when nothing listens.</summary>
        public Activity? Activity { get; }

        /// <summary>Records the operation as succeeded or as a failure carrying the result's error.</summary>
        public void Complete(Result result)
        {
            if (result.IsFailure)
            {
                Failed(result.Error);
            }
            else
            {
                Succeeded();
            }
        }

        /// <summary>Records the operation as succeeded or as a failure carrying the result's error.</summary>
        public void Complete<T>(Result<T> result)
        {
            if (result.IsFailure)
            {
                Failed(result.Error);
            }
            else
            {
                Succeeded();
            }
        }

        /// <summary>Records the operation as succeeded.</summary>
        public void Succeeded() => Complete(WellKnownOutcomes.Success, null);

        /// <summary>Records the operation as cancelled by its caller: not an error, the span status stays unset.</summary>
        public void Canceled() => Complete(WellKnownOutcomes.Canceled, null);

        /// <summary>Records the operation as an expected failure carrying <paramref name="error"/>.</summary>
        public void Failed(Error error)
        {
            string outcome = Activity.RecordFailure(error);
            Complete(outcome, error.Type.ToString());
        }

        /// <summary>
        /// Records an exception the operation ends with: a caller's cancellation is the <c>canceled</c> outcome and not an
        /// error, anything else records the exception event and its full name as <c>error.type</c>.
        /// </summary>
        public void Faulted(Exception exception)
        {
            string outcome = Activity.RecordException(exception);
            string? errorType = outcome == WellKnownOutcomes.Canceled ? null : exception.GetType().FullName ?? exception.GetType().Name;
            Complete(outcome, errorType);
        }

        /// <summary>Ends the span. An operation that never completed (an abandoned stream) records nothing.</summary>
        public void Dispose() => Activity?.Dispose();

        private void Complete(string outcome, string? errorType)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _owner.RecordDuration(_operation, _indexName, _startTimestamp, outcome, errorType);
        }
    }
}
