using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Reporting.Internal;

/// <summary>Log statements of every exporter and converter.</summary>
/// <remarks>
/// EventIds 20000-20099, the <c>SharedKernel.Reporting.Abstractions</c> sub-block of <c>20.Reporting</c>. Never logs
/// row content, object keys or file names.
/// </remarks>
internal static partial class ReportingLog
{
    public const string CategoryName = "SharedKernel.Reporting";

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 0,
        Level = LogLevel.Debug,
        Message = "Report {Operation} started: format {Format}, store {Store}.")]
    public static partial void Started(ILogger logger, string operation, string format, string store);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 1,
        Level = LogLevel.Information,
        Message = "Report {Operation} completed: format {Format}, {RowCount} rows, {SizeBytes} bytes in {DurationMs} ms.")]
    public static partial void Completed(ILogger logger, string operation, string format, long rowCount, long sizeBytes, double durationMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 2,
        Level = LogLevel.Warning,
        Message = "Report {Operation} failed: format {Format}, error {ErrorCode}.")]
    public static partial void Failed(ILogger logger, string operation, string format, string errorCode);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 3,
        Level = LogLevel.Error,
        Message = "Report {Operation} threw: format {Format}.")]
    public static partial void Faulted(ILogger logger, Exception exception, string operation, string format);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 4,
        Level = LogLevel.Debug,
        Message = "Report {Operation} was cancelled: format {Format}.")]
    public static partial void Cancelled(ILogger logger, string operation, string format);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 5,
        Level = LogLevel.Warning,
        Message = "Report stored in store {Store}, but its download link could not be created: error {ErrorCode}.")]
    public static partial void PresignFailed(ILogger logger, string store, string errorCode);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 6,
        Level = LogLevel.Information,
        Message = "PDF conversion completed: {SizeBytes} bytes in {DurationMs} ms.")]
    public static partial void Converted(ILogger logger, long sizeBytes, double durationMs);
}
