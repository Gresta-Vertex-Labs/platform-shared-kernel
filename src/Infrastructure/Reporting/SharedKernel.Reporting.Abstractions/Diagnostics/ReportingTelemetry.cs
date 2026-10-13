using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Reporting.Internal;

/// <summary>
/// The <c>SharedKernel.Reporting</c> spans and instruments, shared by every exporter and converter. Subscribed by the
/// <c>SharedKernel.*</c> wildcard of <c>AddServiceDefaults</c>.
/// </summary>
/// <remarks>
/// Spans and measurements carry the format, the operation, the store and the outcome — never row content, object keys
/// or file names, which often hold personal data. The metric's <c>error.type</c> is bounded (an <see cref="ErrorType"/>
/// name or a CLR type); the error code is on spans only.
/// </remarks>
internal sealed class ReportingTelemetry
{
    public const string ExportOperation = "export";
    public const string ConvertOperation = "convert";

    internal const string DurationInstrumentName = "sharedkernel.reporting.operation.duration";
    internal const string RowsInstrumentName = "sharedkernel.reporting.rows";
    internal const string BytesInstrumentName = "sharedkernel.reporting.bytes";

    private static readonly string Version = TelemetryVersion.Of(typeof(ReportingTelemetry).Assembly);

    private readonly Histogram<double> _duration;
    private readonly Counter<long> _rows;
    private readonly Counter<long> _bytes;

    /// <summary>Creates the instruments on a meter from <paramref name="meterFactory"/>, or a directly created meter when none is registered.</summary>
    public ReportingTelemetry(IMeterFactory? meterFactory)
    {
        var options = new MeterOptions(ReportingDiagnostics.MeterName) { Version = Version };
        Meter meter = meterFactory?.Create(options) ?? new Meter(options);

        _duration = meter.CreateHistogram<double>(
            DurationInstrumentName,
            unit: "s",
            description: "Duration of report exports and PDF conversions, by format, operation and outcome.");
        _rows = meter.CreateCounter<long>(
            RowsInstrumentName,
            unit: "{row}",
            description: "Rows written by successful report exports, by format.");
        _bytes = meter.CreateCounter<long>(
            BytesInstrumentName,
            unit: "By",
            description: "Bytes produced by successful report exports and PDF conversions, by format and operation.");
    }

    public static ActivitySource Source { get; } = new(ReportingDiagnostics.ActivitySourceName, Version);

    public static Activity? Start(string operation, ReportFormat format, string? store)
    {
        Activity? activity = Source.StartActivity($"reporting {operation}", ActivityKind.Internal);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(ReportingTagKeys.Format, format.Name);
            activity.SetTag(ReportingTagKeys.Operation, operation);
            if (store is not null)
            {
                activity.SetTag(ReportingTagKeys.Store, store);
            }
        }

        return activity;
    }

    public void Succeeded(Activity? activity, string operation, ReportFormat format, long startTimestamp, long? rows, long bytes)
    {
        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, Tags(operation, format, WellKnownOutcomes.Success, errorType: null));
        _bytes.Add(bytes, new TagList { { ReportingTagKeys.Format, format.Name }, { ReportingTagKeys.Operation, operation } });
        activity?.SetTag(ReportingTagKeys.SizeBytes, bytes);

        if (rows is { } rowCount)
        {
            _rows.Add(rowCount, new TagList { { ReportingTagKeys.Format, format.Name } });
            activity?.SetTag(ReportingTagKeys.RowCount, rowCount);
        }
    }

    /// <summary>Records a returned failure; returns the <c>error.type</c> value (the <see cref="ErrorType"/> name).</summary>
    public string Failed(Activity? activity, string operation, ReportFormat format, long startTimestamp, Error error)
    {
        string outcome = activity.RecordFailure(error);
        string errorType = error.Type.ToString();
        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, Tags(operation, format, outcome, errorType));
        return errorType;
    }

    /// <summary>Records an exception; returns the outcome. Cancellation is <c>canceled</c> and never an error.</summary>
    public string Faulted(Activity? activity, string operation, ReportFormat format, long startTimestamp, Exception exception)
    {
        string outcome = activity.RecordException(exception);
        string? errorType = outcome == WellKnownOutcomes.Canceled ? null : exception.GetType().FullName ?? exception.GetType().Name;
        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, Tags(operation, format, outcome, errorType));
        return outcome;
    }

    private static TagList Tags(string operation, ReportFormat format, string outcome, string? errorType)
    {
        var tags = new TagList
        {
            { ReportingTagKeys.Format, format.Name },
            { ReportingTagKeys.Operation, operation },
            { WellKnownTagKeys.Outcome, outcome },
        };

        if (errorType is not null)
        {
            tags.Add(WellKnownTagKeys.ErrorType, errorType);
        }

        return tags;
    }
}
