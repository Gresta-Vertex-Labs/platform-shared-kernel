using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Reporting.Gotenberg.Diagnostics;

/// <summary>
/// Log statements of the Gotenberg converter. They carry the HTTP status code and the transport error kind, never the
/// converter's host, route, credentials or response body.
/// </summary>
internal static partial class GotenbergLog
{
    [LoggerMessage(
        EventId = GotenbergLoggingEventIds.Rejected,
        Level = LogLevel.Warning,
        Message = "Gotenberg answered with HTTP status code {StatusCode}.")]
    public static partial void Rejected(ILogger logger, int statusCode);

    [LoggerMessage(
        EventId = GotenbergLoggingEventIds.Unreachable,
        Level = LogLevel.Warning,
        Message = "Gotenberg could not be reached: {HttpRequestError}.")]
    public static partial void Unreachable(ILogger logger, Exception exception, HttpRequestError httpRequestError);
}
