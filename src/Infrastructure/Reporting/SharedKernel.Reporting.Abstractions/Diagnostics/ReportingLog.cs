using Microsoft.Extensions.Logging;

namespace SharedKernel.Reporting.Internal;

/// <summary>Log statements of every exporter and converter.</summary>
/// <remarks>
/// EventIds come from <see cref="ReportingLoggingEventIds"/>. Never logs row content, object keys or file names.
/// </remarks>
internal static partial class ReportingLog
{
    [LoggerMessage(
        EventId = ReportingLoggingEventIds.Started,
        Level = LogLevel.Debug,
        Message = "Report {Operation} for format '{Format}' started.")]
    public static partial void Started(ILogger logger, string operation, string format);

    [LoggerMessage(
        EventId = ReportingLoggingEventIds.Completed,
        Level = LogLevel.Debug,
        Message = "Report {Operation} for format '{Format}' produced {RowCount} rows and {SizeBytes} bytes in {ElapsedMilliseconds} ms.")]
    public static partial void Completed(ILogger logger, string operation, string format, long rowCount, long sizeBytes, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = ReportingLoggingEventIds.Failed,
        Level = LogLevel.Warning,
        Message = "Report {Operation} for format '{Format}' failed with error code '{ErrorCode}' of type '{ErrorType}'.")]
    public static partial void Failed(ILogger logger, string operation, string format, string errorCode, string errorType);

    [LoggerMessage(
        EventId = ReportingLoggingEventIds.Faulted,
        Level = LogLevel.Error,
        Message = "Report {Operation} for format '{Format}' threw an exception.")]
    public static partial void Faulted(ILogger logger, Exception exception, string operation, string format);

    [LoggerMessage(
        EventId = ReportingLoggingEventIds.Cancelled,
        Level = LogLevel.Debug,
        Message = "Report {Operation} for format '{Format}' was cancelled.")]
    public static partial void Cancelled(ILogger logger, string operation, string format);

    [LoggerMessage(
        EventId = ReportingLoggingEventIds.PresignFailed,
        Level = LogLevel.Warning,
        Message = "The report was stored in store '{Store}' but its download link could not be created, error code '{ErrorCode}'.")]
    public static partial void PresignFailed(ILogger logger, string store, string errorCode);

    [LoggerMessage(
        EventId = ReportingLoggingEventIds.Converted,
        Level = LogLevel.Debug,
        Message = "PDF conversion produced {SizeBytes} bytes in {ElapsedMilliseconds} ms.")]
    public static partial void Converted(ILogger logger, long sizeBytes, double elapsedMilliseconds);
}
