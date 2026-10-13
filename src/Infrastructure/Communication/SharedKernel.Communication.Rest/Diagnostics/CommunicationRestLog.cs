using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Rest.Internal;

// Never logs a URL, a host, a header value or a body.
internal static partial class CommunicationRestLog
{
    [LoggerMessage(
        EventId = CommunicationRestLoggingEventIds.PropagationFailed,
        Level = LogLevel.Error,
        Message = "The caller could not be written onto the request, so it goes out without the caller's headers.")]
    public static partial void PropagationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = CommunicationRestLoggingEventIds.ProblemDetailsUnreadable,
        Level = LogLevel.Warning,
        Message = "The problem details body of the response with status {StatusCode} could not be read, so its status decides the error.")]
    public static partial void ProblemDetailsUnreadable(ILogger logger, int statusCode, Exception exception);

    [LoggerMessage(
        EventId = CommunicationRestLoggingEventIds.SuccessBodyUnreadable,
        Level = LogLevel.Warning,
        Message = "The body of the success response could not be read as '{ResponseType}'.")]
    public static partial void SuccessBodyUnreadable(ILogger logger, string responseType, Exception exception);
}
