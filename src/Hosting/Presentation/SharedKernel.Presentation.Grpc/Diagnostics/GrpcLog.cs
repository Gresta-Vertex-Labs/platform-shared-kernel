using global::Grpc.Core;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Presentation.Grpc.Diagnostics;

/// <summary>The log statements of this package. Never logs a status detail, a trailer or any caller data.</summary>
internal static partial class GrpcLog
{
    [LoggerMessage(
        EventId = GrpcLoggingEventIds.ServerError,
        Level = LogLevel.Error,
        Message = "gRPC method '{GrpcMethod}' failed with status {StatusCode} and code '{ErrorCode}'.")]
    public static partial void ServerError(ILogger logger, string grpcMethod, StatusCode statusCode, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = GrpcLoggingEventIds.ClientError,
        Level = LogLevel.Debug,
        Message = "gRPC method '{GrpcMethod}' was rejected with status {StatusCode} and code '{ErrorCode}'.")]
    public static partial void ClientError(ILogger logger, string grpcMethod, StatusCode statusCode, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = GrpcLoggingEventIds.CallCancelled,
        Level = LogLevel.Debug,
        Message = "The call to gRPC method '{GrpcMethod}' was cancelled before it completed.")]
    public static partial void CallCancelled(ILogger logger, string grpcMethod, Exception exception);
}
