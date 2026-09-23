using Grpc.Core;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.Grpc.Errors;

/// <summary>
/// Maps an <see cref="ErrorType"/> to the gRPC <see cref="StatusCode"/> a failed call ends with.
/// </summary>
/// <remarks>
/// <para>
/// The gRPC counterpart of <c>SharedKernel.Presentation.WebApi.Errors.ErrorTypeStatusCodeMap</c>, never a merge with
/// it: both key off the same <see cref="ErrorType"/>, but HTTP and gRPC status codes do not correspond one to one
/// (gRPC's <see cref="StatusCode.Aborted"/> and <see cref="StatusCode.FailedPrecondition"/> cover ground HTTP splits
/// across 409, 412 and 422).
/// </para>
/// <para>
/// Every error the interceptor of <c>AddSharedKernelGrpc()</c> maps and every failure thrown by
/// <see cref="GrpcResultExtensions"/> takes its code from here; never switch on <see cref="ErrorType"/> by hand.
/// </para>
/// </remarks>
public static class GrpcStatusCodeMap
{
    /// <summary>Returns the gRPC status code for <paramref name="type"/>.</summary>
    /// <param name="type">The error type.</param>
    /// <returns>
    /// <list type="table">
    ///   <item><term><see cref="ErrorType.Validation"/></term><description><see cref="StatusCode.InvalidArgument"/></description></item>
    ///   <item><term><see cref="ErrorType.Unauthorized"/></term><description><see cref="StatusCode.Unauthenticated"/></description></item>
    ///   <item><term><see cref="ErrorType.Forbidden"/></term><description><see cref="StatusCode.PermissionDenied"/></description></item>
    ///   <item><term><see cref="ErrorType.NotFound"/></term><description><see cref="StatusCode.NotFound"/></description></item>
    ///   <item><term><see cref="ErrorType.Conflict"/></term><description><see cref="StatusCode.Aborted"/>: gRPC documents
    ///   it for concurrency conflicts such as a failed version check.</description></item>
    ///   <item><term><see cref="ErrorType.BusinessRule"/></term><description><see cref="StatusCode.FailedPrecondition"/>:
    ///   the system is not in the state the operation requires.</description></item>
    ///   <item><term><see cref="ErrorType.Unexpected"/></term><description><see cref="StatusCode.Internal"/></description></item>
    ///   <item><term><see cref="ErrorType.Unavailable"/></term><description><see cref="StatusCode.Unavailable"/>: a
    ///   dependency is down or throttling; the client may retry later.</description></item>
    ///   <item><term><see cref="ErrorType.Timeout"/></term><description><see cref="StatusCode.DeadlineExceeded"/>: the
    ///   operation did not complete in time.</description></item>
    ///   <item><term>Anything else, <see cref="ErrorType.None"/> included</term><description><see cref="StatusCode.Unknown"/>,
    ///   gRPC's status for an error no other code describes.</description></item>
    /// </list>
    /// </returns>
    public static StatusCode Resolve(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCode.InvalidArgument,
        ErrorType.Unauthorized => StatusCode.Unauthenticated,
        ErrorType.Forbidden => StatusCode.PermissionDenied,
        ErrorType.NotFound => StatusCode.NotFound,
        ErrorType.Conflict => StatusCode.Aborted,
        ErrorType.BusinessRule => StatusCode.FailedPrecondition,
        ErrorType.Unexpected => StatusCode.Internal,
        ErrorType.Unavailable => StatusCode.Unavailable,
        ErrorType.Timeout => StatusCode.DeadlineExceeded,
        _ => StatusCode.Unknown,
    };
}
