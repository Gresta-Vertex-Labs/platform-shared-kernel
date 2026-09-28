using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using SharedKernel.Primitives.Errors;
using GrpcCore = global::Grpc.Core;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// Builds the <see cref="GrpcCore.AsyncUnaryCall{TResponse}"/> a generated client's method returns, for mocking the
/// client (its methods are virtual) in a unit test.
/// </summary>
/// <example>
/// <code>
/// var client = Substitute.For&lt;Inventory.InventoryClient&gt;();
/// client.GetStockAsync(Arg.Any&lt;StockRequest&gt;(), Arg.Any&lt;CallOptions&gt;())
///     .Returns(GrpcCalls.Failure&lt;StockReply&gt;(Error.NotFound("inventory.sku_not_found", "No such SKU.")));
/// </code>
/// </example>
public static class GrpcCalls
{
    /// <summary>The <c>ErrorInfo</c> domain the failures carry.</summary>
    public const string ErrorDomain = "sharedkernel.testing";

    /// <summary>A call that returns <paramref name="response"/>.</summary>
    /// <typeparam name="TResponse">The response message.</typeparam>
    /// <param name="response">The response.</param>
    /// <returns>The call.</returns>
    public static GrpcCore.AsyncUnaryCall<TResponse> Success<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new GrpcCore.Metadata()),
            static () => GrpcCore.Status.DefaultSuccess,
            static () => [],
            static () => { });

    /// <summary>A call that fails with a bare status, as a service outside the platform or the gRPC client would.</summary>
    /// <typeparam name="TResponse">The response message.</typeparam>
    /// <param name="statusCode">The status.</param>
    /// <param name="detail">The status detail.</param>
    /// <returns>The call.</returns>
    public static GrpcCore.AsyncUnaryCall<TResponse> Failure<TResponse>(GrpcCore.StatusCode statusCode, string detail = "") =>
        Failed<TResponse>(new GrpcCore.RpcException(new GrpcCore.Status(statusCode, detail)));

    /// <summary>
    /// A call that fails the way a platform service answers <paramref name="error"/>: the status of its
    /// <see cref="ErrorType"/>, an <c>ErrorInfo</c> with its code, and a <c>BadRequest</c> of its field errors.
    /// </summary>
    /// <typeparam name="TResponse">The response message.</typeparam>
    /// <param name="error">The error.</param>
    /// <returns>The call.</returns>
    public static GrpcCore.AsyncUnaryCall<TResponse> Failure<TResponse>(Error error) =>
        Failed<TResponse>(ToRpcException(error));

    /// <summary>The <see cref="GrpcCore.RpcException"/> a platform service's rich status for <paramref name="error"/> arrives as.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The exception, with the status in its trailers.</returns>
    public static GrpcCore.RpcException ToRpcException(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = new Google.Rpc.Status { Code = (int)StatusCodeFor(error.Type), Message = error.Message };
        status.Details.Add(Any.Pack(new ErrorInfo { Reason = error.Code, Domain = ErrorDomain }));

        if (error.Details is { Count: > 0 } details)
        {
            var badRequest = new BadRequest();
            foreach (Error field in details)
            {
                badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation
                {
                    Field = field.MessageArguments?.GetValueOrDefault(ErrorArgumentNames.PropertyPath)?.ToString() ?? field.Code,
                    Reason = field.Code,
                    Description = field.Message,
                });
            }

            status.Details.Add(Any.Pack(badRequest));
        }

        return GrpcCore.RpcStatusExtensions.ToRpcException(status);
    }

    private static GrpcCore.AsyncUnaryCall<TResponse> Failed<TResponse>(GrpcCore.RpcException exception) =>
        new(
            Task.FromException<TResponse>(exception),
            Task.FromResult(new GrpcCore.Metadata()),
            () => exception.Status,
            () => exception.Trailers,
            static () => { });

    // 14.Presentation's GrpcStatusCodeMap, forward: a test double may not reference the host package.
    private static GrpcCore.StatusCode StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => GrpcCore.StatusCode.InvalidArgument,
        ErrorType.Unauthorized => GrpcCore.StatusCode.Unauthenticated,
        ErrorType.Forbidden => GrpcCore.StatusCode.PermissionDenied,
        ErrorType.NotFound => GrpcCore.StatusCode.NotFound,
        ErrorType.Conflict => GrpcCore.StatusCode.Aborted,
        ErrorType.BusinessRule => GrpcCore.StatusCode.FailedPrecondition,
        ErrorType.Unavailable => GrpcCore.StatusCode.Unavailable,
        ErrorType.Timeout => GrpcCore.StatusCode.DeadlineExceeded,
        ErrorType.Unexpected => GrpcCore.StatusCode.Internal,
        _ => GrpcCore.StatusCode.Unknown,
    };
}
