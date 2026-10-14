using Microsoft.Extensions.Logging;

namespace SharedKernel.Presentation.SignalR.Diagnostics;

/// <summary>The log statements of this package. Never logs a hub argument, a message or any caller data.</summary>
internal static partial class SignalRLog
{
    [LoggerMessage(
        EventId = SignalRLoggingEventIds.InvocationRateLimited,
        Level = LogLevel.Warning,
        Message = "Rate limiting refused an invocation of hub method '{HubMethodName}' of hub '{HubName}'.")]
    public static partial void InvocationRateLimited(ILogger logger, string hubName, string hubMethodName);

    [LoggerMessage(
        EventId = SignalRLoggingEventIds.ServerError,
        Level = LogLevel.Error,
        Message = "Hub method '{HubMethodName}' of hub '{HubName}' failed with code '{ErrorCode}'.")]
    public static partial void ServerError(ILogger logger, string hubName, string hubMethodName, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = SignalRLoggingEventIds.ClientError,
        Level = LogLevel.Debug,
        Message = "Hub method '{HubMethodName}' of hub '{HubName}' was rejected with code '{ErrorCode}'.")]
    public static partial void ClientError(ILogger logger, string hubName, string hubMethodName, string errorCode, Exception? exception);

    [LoggerMessage(
        EventId = SignalRLoggingEventIds.ConnectionAborted,
        Level = LogLevel.Debug,
        Message = "The connection closed before hub method '{HubMethodName}' of hub '{HubName}' completed.")]
    public static partial void ConnectionAborted(ILogger logger, string hubName, string hubMethodName, Exception exception);

    [LoggerMessage(
        EventId = SignalRLoggingEventIds.StreamInResult,
        Level = LogLevel.Error,
        Message = "Hub method '{HubMethodName}' of hub '{HubName}' returned a stream inside a Result.")]
    public static partial void StreamInResult(ILogger logger, string hubName, string hubMethodName, Exception exception);

    [LoggerMessage(
        EventId = SignalRLoggingEventIds.ServerFailure,
        Level = LogLevel.Warning,
        Message = "Hub method '{HubMethodName}' of hub '{HubName}' returned a failure with code '{ErrorCode}'.")]
    public static partial void ServerFailure(ILogger logger, string hubName, string hubMethodName, string errorCode);
}
