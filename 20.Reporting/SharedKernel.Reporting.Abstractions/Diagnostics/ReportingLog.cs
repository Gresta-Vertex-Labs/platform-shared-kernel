using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Reporting.Abstractions.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <see cref="Delivery.StorageStreamingWriter"/>,
/// shared by every provider package.
/// </summary>
/// <remarks>
/// Occupies EventId sub-block <c>LoggingEventIdRanges.Reporting + 0</c>..<c>+99</c> (20000-20099) —
/// the first 100-wide sub-block of this domain's <c>20000-20999</c> block, per
/// <c>SharedKernel.Reporting.Abstractions</c>'s declaration order in <c>20.Reporting/CLAUDE.md</c>.
/// Never logs row content or a formatted cell value — see
/// <see cref="Exporters.IReportExporter{TRow}"/>'s PII documentation.
/// </remarks>
internal static partial class ReportingLog
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 0,
        Level = LogLevel.Debug,
        Message = "Report export started to store '{Store}' key '{Key}'.")]
    internal static partial void ExportStarted(ILogger logger, string store, string key);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 1,
        Level = LogLevel.Information,
        Message = "Report export completed to store '{Store}' key '{Key}': {RowCount} rows in {DurationMs}ms.")]
    internal static partial void ExportCompleted(ILogger logger, string store, string key, long rowCount, double durationMs);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Reporting + 2,
        Level = LogLevel.Warning,
        Message = "Report export failed to store '{Store}' key '{Key}'.")]
    internal static partial void ExportFailed(ILogger logger, Exception exception, string store, string key);
}
