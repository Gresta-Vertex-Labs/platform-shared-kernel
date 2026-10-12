using Microsoft.Extensions.Logging;

namespace SharedKernel.Presentation.GraphQL.Diagnostics;

/// <summary>The log statements of this package. Never logs a message, a variable, a query or any caller data.</summary>
internal static partial class GraphQLLog
{
    [LoggerMessage(
        EventId = GraphQLLoggingEventIds.ServerError,
        Level = LogLevel.Error,
        Message = "GraphQL error '{ErrorCode}' with status {StatusCode}.")]
    public static partial void ServerError(ILogger logger, string errorCode, int statusCode, Exception? exception);

    [LoggerMessage(
        EventId = GraphQLLoggingEventIds.ClientError,
        Level = LogLevel.Debug,
        Message = "GraphQL error '{ErrorCode}' with status {StatusCode}.")]
    public static partial void ClientError(ILogger logger, string errorCode, int statusCode, Exception? exception);
}
