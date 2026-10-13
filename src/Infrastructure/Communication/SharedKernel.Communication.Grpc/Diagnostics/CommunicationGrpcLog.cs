using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Grpc.Internal;

// Never logs metadata values, an address or a message body.
internal static partial class CommunicationGrpcLog
{
    [LoggerMessage(
        EventId = CommunicationGrpcLoggingEventIds.PropagationFailed,
        Level = LogLevel.Error,
        Message = "The caller could not be written onto the gRPC call '{GrpcMethod}', so it goes out without the caller's metadata.")]
    public static partial void PropagationFailed(ILogger logger, string grpcMethod, Exception exception);
}
