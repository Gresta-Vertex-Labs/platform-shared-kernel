using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Storage.S3.Diagnostics;

/// <summary>
/// The <see cref="StorageTelemetryNames.SourceName"/> activity source and meter of the S3 and OBS providers. The host's
/// <c>SharedKernel.*</c> subscription (<c>AddServiceDefaults</c>) picks both up; nothing references this type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spans</b> are <c>storage {operation}</c>, kind Client, tagged with the store, the operation and the provider
/// connection name. Failures are recorded only through the shared <c>Primitives</c> recorder, so a <c>Result</c>
/// failure carries <c>error.code</c> (the <c>storage.*</c> code) and <c>error.type</c> (the <c>ErrorType</c> name), an
/// unexpected exception carries its full exception event, and a caller's cancellation is never an error.
/// </para>
/// <para>
/// <b>Instruments</b> are created on a meter from <see cref="IMeterFactory"/>. Object keys, buckets and endpoints are
/// never a tag: keys often embed user data such as file names.
/// </para>
/// </remarks>
internal sealed class StorageTelemetry
{
    /// <summary>The source and meter name, declared here as a public const of this assembly (equal to <see cref="StorageTelemetryNames.SourceName"/>).</summary>
    public const string Name = StorageTelemetryNames.SourceName;

    internal const string UploadDirection = "upload";
    internal const string DownloadDirection = "download";

    private static readonly string Version = TelemetryVersion.Of(typeof(StorageTelemetry).Assembly);

    private readonly Histogram<double> _duration;
    private readonly Counter<long> _bytes;

    /// <summary>Initialises the telemetry over <paramref name="meterFactory"/>.</summary>
    public StorageTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        Meter meter = meterFactory.Create(new MeterOptions(Name) { Version = Version });
        _duration = meter.CreateHistogram<double>(
            StorageTelemetryNames.OperationDurationInstrumentName,
            unit: "s",
            description: "Duration of storage operations, by store, operation, provider and outcome.");
        _bytes = meter.CreateCounter<long>(
            StorageTelemetryNames.BytesInstrumentName,
            unit: "By",
            description: "Bytes uploaded and downloaded, by store and direction. Downloads count the bytes requested.");
    }

    /// <summary>Gets the activity source shared by every provider operation.</summary>
    internal static ActivitySource Source { get; } = new(Name, Version);

    /// <summary>Starts the span of one operation, or returns <see langword="null"/> when nothing listens.</summary>
    public static Activity? StartActivity(string operation, string store, string provider)
    {
        Activity? activity = Source.StartActivity($"storage {operation}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(StorageTagKeys.Store, store);
            activity.SetTag(StorageTagKeys.Operation, operation);
            activity.SetTag(StorageTagKeys.Provider, provider);
        }

        return activity;
    }

    /// <summary>
    /// Records the duration of one operation with its outcome. <paramref name="errorType"/> and
    /// <paramref name="errorCode"/> are given on failures only.
    /// </summary>
    public void RecordDuration(
        string operation,
        string store,
        string provider,
        long startTimestamp,
        string outcome,
        string? errorType = null,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { StorageTagKeys.Store, store },
            { StorageTagKeys.Operation, operation },
            { StorageTagKeys.Provider, provider },
            { WellKnownTagKeys.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorType);
        }

        if (errorCode is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorCode, errorCode);
        }

        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    /// <summary>Counts uploaded or downloaded bytes.</summary>
    public void RecordBytes(string store, string provider, string direction, long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        _bytes.Add(
            bytes,
            new TagList
            {
                { StorageTagKeys.Store, store },
                { StorageTagKeys.Provider, provider },
                { StorageTagKeys.Direction, direction },
            });
    }
}
