using Grpc.Core;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.Errors;

/// <summary>
/// Single source of truth for mapping an <see cref="ErrorType"/> to a gRPC <see cref="StatusCode"/>.
/// </summary>
/// <remarks>
/// <para>
/// A sibling to, never a merge with,
/// <see cref="ErrorTypeStatusCodeMap"/>. HTTP status codes and
/// gRPC <see cref="StatusCode"/> are different target enums with no clean 1:1 correspondence — HTTP's
/// 422 has no gRPC analogue, and gRPC's <see cref="StatusCode.Aborted"/>/<see cref="StatusCode.FailedPrecondition"/>
/// cover ground HTTP splits across 409/412/422. Both maps key off the same <c>01.Core</c>
/// <see cref="ErrorType"/> enum — that shared vocabulary is the generalization point; merging the
/// two value tables would force a false equivalence between two genuinely different protocols.
/// </para>
/// <para>
/// Any inline switch statement duplicating this mapping anywhere else in a consuming service is a
/// platform violation — always call <see cref="Resolve"/> (directly, or indirectly via
/// <c>GrpcResultExtensions</c>/<c>Interceptors.GrpcExceptionInterceptor</c>) instead.
/// </para>
/// </remarks>
public static class GrpcStatusCodeMap
{
    /// <summary>
    /// Resolves the gRPC <see cref="StatusCode"/> that corresponds to the specified
    /// <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The error type to resolve.</param>
    /// <returns>
    /// The mapped <see cref="StatusCode"/>: <see cref="ErrorType.Validation"/> →
    /// <see cref="StatusCode.InvalidArgument"/>, <see cref="ErrorType.Unauthorized"/> →
    /// <see cref="StatusCode.Unauthenticated"/>, <see cref="ErrorType.Forbidden"/> →
    /// <see cref="StatusCode.PermissionDenied"/>, <see cref="ErrorType.NotFound"/> →
    /// <see cref="StatusCode.NotFound"/>, <see cref="ErrorType.Conflict"/> →
    /// <see cref="StatusCode.Aborted"/> (gRPC's own <c>Aborted</c> status is documented for "a
    /// concurrency issue such as a sequencer check failure or transaction abort" — a closer
    /// semantic fit for <see cref="ErrorType.Conflict"/> than <see cref="StatusCode.AlreadyExists"/>),
    /// <see cref="ErrorType.BusinessRule"/> → <see cref="StatusCode.FailedPrecondition"/> (the
    /// closest gRPC analogue to HTTP 422 — "the system is not in a state required for the
    /// operation's execution"), <see cref="ErrorType.Unexpected"/> → <see cref="StatusCode.Internal"/>.
    /// Any <see cref="ErrorType"/> not explicitly mapped (including <see cref="ErrorType.None"/>)
    /// falls back to <see cref="StatusCode.Unknown"/> — gRPC's own "no more specific error is
    /// applicable" status, the protocol-native analogue of the HTTP map's 500 fallback.
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
        _ => StatusCode.Unknown,
    };
}
