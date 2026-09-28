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
/// <c>GrpcErrorCodes.ForStatus</c> gives a status that carries no code of its own.
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
        StatusCode.OK => "grpc.ok",
        StatusCode.Cancelled => "grpc.cancelled",
        StatusCode.Unknown => "grpc.unknown",
        StatusCode.InvalidArgument => "grpc.invalid_argument",
        StatusCode.DeadlineExceeded => "grpc.deadline_exceeded",
        StatusCode.NotFound => "grpc.not_found",
        StatusCode.AlreadyExists => "grpc.already_exists",
        StatusCode.PermissionDenied => "grpc.permission_denied",
        StatusCode.ResourceExhausted => "grpc.resource_exhausted",
        StatusCode.FailedPrecondition => "grpc.failed_precondition",
        StatusCode.Aborted => "grpc.aborted",
        StatusCode.OutOfRange => "grpc.out_of_range",
        StatusCode.Unimplemented => "grpc.unimplemented",
        StatusCode.Internal => "grpc.internal",
        StatusCode.Unavailable => "grpc.unavailable",
        StatusCode.DataLoss => "grpc.data_loss",
        StatusCode.Unauthenticated => "grpc.unauthenticated",
        _ => "grpc." + ((int)statusCode).ToString(CultureInfo.InvariantCulture),
    };

    public static Error Create(ErrorType type, string code, string message) => type switch
    {
        ErrorType.Validation => Error.Validation(code, message),
        ErrorType.Unauthorized => Error.Unauthorized(code, message),
        ErrorType.Forbidden => Error.Forbidden(code, message),
        ErrorType.NotFound => Error.NotFound(code, message),
        ErrorType.Conflict => Error.Conflict(code, message),
        ErrorType.BusinessRule => Error.BusinessRule(code, message),
        ErrorType.Unavailable => Error.Unavailable(code, message),
        ErrorType.Timeout => Error.Timeout(code, message),
        _ => Error.Unexpected(code, message),
    };
}
