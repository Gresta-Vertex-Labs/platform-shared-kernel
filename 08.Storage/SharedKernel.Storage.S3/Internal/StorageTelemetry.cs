using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Storage.S3.Internal;

/// <summary>
/// The <c>SharedKernel.Storage</c> activity source and meter. Wired into OpenTelemetry by
/// <c>WithStorageTelemetry()</c> in <c>SharedKernel.ServiceDefaults</c>.
/// </summary>
/// <remarks>
/// Spans and measurements carry the store, the operation, the provider connection and, on failure, the storage
/// error code. Object keys are never recorded: they often embed user data such as file names.
/// </remarks>
internal static class StorageTelemetry
{
    public const string InstrumentationName = "SharedKernel.Storage";

    public const string StoreTag = "storage.store";
    public const string OperationTag = "storage.operation";
    public const string ProviderTag = "storage.provider";
    public const string DirectionTag = "storage.direction";
    public const string Upload = "upload";
    public const string Download = "download";

    private static readonly string Version =
        typeof(StorageTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static ActivitySource Source { get; } = new(InstrumentationName, Version);

    private static Meter Meter { get; } = new(InstrumentationName, Version);

    private static Histogram<double> Duration { get; } = Meter.CreateHistogram<double>(
        "storage.client.operation.duration",
        unit: "s",
        description: "Duration of storage operations, by store, operation and outcome.");

    private static Counter<long> Bytes { get; } = Meter.CreateCounter<long>(
        "storage.client.bytes",
        unit: "By",
        description: "Bytes uploaded and downloaded, by store and direction. Downloads count the bytes requested.");

    public static Activity? StartActivity(string operation, string store, string provider)
    {
        Activity? activity = Source.StartActivity($"storage {operation}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag(StoreTag, store);
            activity.SetTag(OperationTag, operation);
            activity.SetTag(ProviderTag, provider);
        }

        return activity;
    }

    public static void Complete(Activity? activity, string operation, string store, string provider, long startTimestamp, string? errorCode)
    {
        var tags = new TagList
        {
            { StoreTag, store },
            { OperationTag, operation },
            { ProviderTag, provider },
        };

        if (errorCode is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorCode);
            activity?.SetTag(WellKnownTagKeys.ErrorType, errorCode);
            activity?.SetStatus(ActivityStatusCode.Error, errorCode);
        }

        Duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    public static void RecordBytes(string store, string provider, string direction, long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        Bytes.Add(bytes, new TagList { { StoreTag, store }, { ProviderTag, provider }, { DirectionTag, direction } });
    }
}
