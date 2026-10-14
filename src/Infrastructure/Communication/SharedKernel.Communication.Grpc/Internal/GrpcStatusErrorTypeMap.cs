using System.Globalization;
using Grpc.Core;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Grpc.Internal;

/// <summary>
/// Maps a gRPC status back to the <see cref="ErrorType"/> that produced it: the reverse of <c>14.Presentation</c>'s
/// <c>GrpcStatusCodeMap</c>, plus the statuses a server answers outside it.
/// </summary>
/// <remarks>
/// Kept by hand in step with the forward map, since no package may hold both directions (<c>11.Communication</c> never
/// references <c>14.Presentation</c>). Codes: <c>grpc.{status}</c>, the names <c>14.Presentation</c>'s
/// <c>GrpcErrorCodes.ForStatus</c> gives a status that carries no code of its own, declared in
/// <see cref="CommunicationErrorCodes.GrpcStatus"/>.
/// </remarks>
internal static class GrpcStatusErrorTypeMap
{
    public static ErrorType Resolve(StatusCode statusCode) => statusCode switch
    {
        StatusCode.InvalidArgument or StatusCode.OutOfRange => ErrorType.Validation,
        StatusCode.Unauthenticated => ErrorType.Unauthorized,
        StatusCode.PermissionDenied => ErrorType.Forbidden,
        StatusCode.NotFound => ErrorType.NotFound,
        StatusCode.Aborted or StatusCode.AlreadyExists => ErrorType.Conflict,
        StatusCode.FailedPrecondition => ErrorType.BusinessRule,
        StatusCode.Unavailable or StatusCode.ResourceExhausted => ErrorType.Unavailable,
        StatusCode.DeadlineExceeded => ErrorType.Timeout,
        _ => ErrorType.Unexpected,
    };

    public static string CodeFor(StatusCode statusCode) => statusCode switch
    {
        StatusCode.OK => CommunicationErrorCodes.GrpcStatus.Ok,
        StatusCode.Cancelled => CommunicationErrorCodes.GrpcStatus.Cancelled,
        StatusCode.Unknown => CommunicationErrorCodes.GrpcStatus.Unknown,
        StatusCode.InvalidArgument => CommunicationErrorCodes.GrpcStatus.InvalidArgument,
        StatusCode.DeadlineExceeded => CommunicationErrorCodes.GrpcStatus.DeadlineExceeded,
        StatusCode.NotFound => CommunicationErrorCodes.GrpcStatus.NotFound,
        StatusCode.AlreadyExists => CommunicationErrorCodes.GrpcStatus.AlreadyExists,
        StatusCode.PermissionDenied => CommunicationErrorCodes.GrpcStatus.PermissionDenied,
        StatusCode.ResourceExhausted => CommunicationErrorCodes.GrpcStatus.ResourceExhausted,
        StatusCode.FailedPrecondition => CommunicationErrorCodes.GrpcStatus.FailedPrecondition,
        StatusCode.Aborted => CommunicationErrorCodes.GrpcStatus.Aborted,
        StatusCode.OutOfRange => CommunicationErrorCodes.GrpcStatus.OutOfRange,
        StatusCode.Unimplemented => CommunicationErrorCodes.GrpcStatus.Unimplemented,
        StatusCode.Internal => CommunicationErrorCodes.GrpcStatus.Internal,
        StatusCode.Unavailable => CommunicationErrorCodes.GrpcStatus.Unavailable,
        StatusCode.DataLoss => CommunicationErrorCodes.GrpcStatus.DataLoss,
        StatusCode.Unauthenticated => CommunicationErrorCodes.GrpcStatus.Unauthenticated,
        _ => CommunicationErrorCodes.GrpcStatus.NumberPrefix + ((int)statusCode).ToString(CultureInfo.InvariantCulture),
    };
}
