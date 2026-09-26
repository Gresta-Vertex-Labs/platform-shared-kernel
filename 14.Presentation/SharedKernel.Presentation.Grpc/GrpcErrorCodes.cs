using System.Globalization;
using Grpc.Core;

namespace SharedKernel.Presentation.Grpc;

/// <summary>
/// The error codes this package itself puts in a status's <c>ErrorInfo</c> <c>reason</c> or a field violation's
/// <c>reason</c>, for outcomes that carry no <see cref="SharedKernel.Primitives.Errors.Error"/> of the service.
/// </summary>
/// <remarks>
/// Every other code comes from the error the service returned or threw. Like those, these values are a contract:
/// clients branch on them and translations are keyed by them. They are the gRPC counterparts of the HTTP core's
/// <c>PresentationErrorCodes</c>.
/// </remarks>
public static class GrpcErrorCodes
{
    /// <summary>
    /// <c>grpc.more_field_violations</c>: the <c>reason</c> (and <c>field</c>) of the last field violation of a
    /// <c>BadRequest</c> detail when there were more field errors than a status can carry. Its description says how
    /// many were left out (<c>"… and 350 more."</c>, translated under this code with a <c>{count}</c> placeholder).
    /// </summary>
    /// <remarks>
    /// A status travels in HTTP/2 trailers, which clients cap: 8 KB by default for many gRPC clients, 64 KB for
    /// <c>Grpc.Net.Client</c>. A status carries at most 50 field violations, in about 3 KB, so it always arrives.
    /// </remarks>
    public const string MoreFieldViolations = "grpc.more_field_violations";

    /// <summary>The prefix of <see cref="ForStatus"/> codes.</summary>
    private const string StatusPrefix = "grpc.";

    /// <summary>
    /// Returns the code of a status this service did not build from an error — an <see cref="RpcException"/> thrown
    /// by the service's own code or by a gRPC call it made to another service: <c>grpc.</c> followed by the status's
    /// canonical name in lower case, such as <c>grpc.resource_exhausted</c> or <c>grpc.unavailable</c>.
    /// </summary>
    /// <param name="statusCode">The gRPC status code.</param>
    /// <returns>
    /// <c>grpc.{name}</c> for the seventeen codes of the gRPC specification (<c>grpc.ok</c>, <c>grpc.cancelled</c>,
    /// <c>grpc.unknown</c>, <c>grpc.invalid_argument</c>, <c>grpc.deadline_exceeded</c>, <c>grpc.not_found</c>,
    /// <c>grpc.already_exists</c>, <c>grpc.permission_denied</c>, <c>grpc.resource_exhausted</c>,
    /// <c>grpc.failed_precondition</c>, <c>grpc.aborted</c>, <c>grpc.out_of_range</c>, <c>grpc.unimplemented</c>,
    /// <c>grpc.internal</c>, <c>grpc.unavailable</c>, <c>grpc.data_loss</c>, <c>grpc.unauthenticated</c>);
    /// <c>grpc.{number}</c> for any other value.
    /// </returns>
    internal static string ForStatus(StatusCode statusCode) => statusCode switch
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
        _ => StatusPrefix + ((int)statusCode).ToString(CultureInfo.InvariantCulture),
    };
}
