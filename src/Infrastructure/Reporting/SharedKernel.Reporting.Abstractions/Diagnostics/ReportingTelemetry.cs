using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Reporting.Internal;

/// <summary>
/// The <c>SharedKernel.Reporting</c> activity source and meter, shared by every exporter and converter. Wired into
/// OpenTelemetry by <c>WithReportingTelemetry()</c> in <c>SharedKernel.ServiceDefaults</c>.
/// </summary>
/// <remarks>
/// Spans and measurements carry the format, the operation, the store and, on failure, the error code — never row
/// content, object keys or file names, which often hold personal data.
/// </remarks>
internal static class ReportingTelemetry
{
    public const string InstrumentationName = "SharedKernel.Reporting";

    public const string FormatTag = "reporting.format";
    public const string OperationTag = "reporting.operation";
    public const string StoreTag = "reporting.store";
    public const string RowCountTag = "reporting.row_count";
    public const string SizeTag = "reporting.size_bytes";

    public const string ExportOperation = "export";
    public const string ConvertOperation = "convert";

    private const string CancelledErrorType = "cancelled";

    private static readonly string Version =
        typeof(ReportingTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static ActivitySource Source { get; } = new(InstrumentationName, Version);

    private static Meter Meter { get; } = new(InstrumentationName, Version);

    private static Histogram<double> Duration { get; } = Meter.CreateHistogram<double>(
        "reporting.operation.duration",
        unit: "s",
        description: "Duration of report exports and PDF conversions, by format, operation and outcome.");

    private static Counter<long> Rows { get; } = Meter.CreateCounter<long>(
        "reporting.rows",
        unit: "{row}",
        description: "Rows written by successful report exports, by format.");

    private static Counter<long> Bytes { get; } = Meter.CreateCounter<long>(
        "reporting.bytes",
        unit: "By",
        description: "Bytes produced by successful report exports and PDF conversions, by format and operation.");

    public static Activity? Start(string operation, ReportFormat format, string? store)
    {
        Activity? activity = Source.StartActivity($"reporting {operation}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(FormatTag, format.Name);
            activity.SetTag(OperationTag, operation);
            if (store is not null)
            {
                activity.SetTag(StoreTag, store);
            }
        }

        return activity;
    }

    public static void Succeeded(Activity? activity, string operation, ReportFormat format, long startTimestamp, long? rows, long bytes)
    {
        var tags = Tags(operation, format);
        Duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
        Bytes.Add(bytes, tags);
        activity?.SetTag(SizeTag, bytes);

        if (rows is { } rowCount)
        {
            Rows.Add(rowCount, new KeyValuePair<string, object?>(FormatTag, format.Name));
            activity?.SetTag(RowCountTag, rowCount);
        }
    }

    public static void Failed(Activity? activity, string operation, ReportFormat format, long startTimestamp, string errorType)
    {
        var tags = Tags(operation, format);
        tags.Add(WellKnownTagKeys.ErrorType, errorType);
        Duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);

        if (activity is not null)
        {
            activity.SetTag(WellKnownTagKeys.ErrorType, errorType);
            activity.SetStatus(ActivityStatusCode.Error, errorType);
        }
    }

    public static void Cancelled(Activity? activity, string operation, ReportFormat format, long startTimestamp) =>
        Failed(activity, operation, format, startTimestamp, CancelledErrorType);

    private static TagList Tags(string operation, ReportFormat format) => new()
    {
        { FormatTag, format.Name },
        { OperationTag, operation },
    };
}
