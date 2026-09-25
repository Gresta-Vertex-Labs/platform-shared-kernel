using Grpc.Core;
using SharedKernel.Presentation.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.Grpc.Results;

/// <summary>
/// Converts <see cref="Result"/>/<see cref="Result{T}"/> outcomes into <see cref="RpcException"/>
/// throws — the gRPC-boundary sibling to
/// <c>SharedKernel.Presentation.WebApi.Results.ResultHttpExtensions</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the canonical <c>Result&lt;T&gt;</c>→gRPC mapping — a gRPC service method must never
/// hand-construct <c>new RpcException(new Status(...))</c> or branch on <c>IsSuccess</c> inline;
/// always call <see cref="ToGrpcResult"/>/<see cref="ToGrpcResult{T}"/> instead.
/// </para>
/// <para>
/// This package never references <c>04.Contracts</c> at all (D-78) — protobuf-generated messages
/// are this package's only wire-contract surface, and a failure travels as the
/// <see cref="RpcException"/> status rather than as a response DTO.
/// </para>
/// </remarks>
public static class GrpcResultExtensions
{
    /// <summary>
    /// Converts a non-generic <see cref="Result"/> to a gRPC-boundary outcome.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <exception cref="RpcException">
    /// Thrown on failure, carrying a <see cref="Status"/> whose <see cref="StatusCode"/> is
    /// resolved via <see cref="GrpcStatusCodeMap.Resolve"/> and whose detail is
    /// <see cref="SharedKernel.Primitives.Errors.Error.Message"/>. Does nothing on success.
    /// </exception>
    public static void ToGrpcResult(this Result result)
    {
        if (result.IsFailure)
        {
            throw BuildRpcException(result.Error);
        }
    }

    /// <summary>
    /// Converts a <see cref="Result{T}"/> to a gRPC-boundary outcome.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>The unwrapped success value.</returns>
    /// <exception cref="RpcException">
    /// Thrown on failure, carrying a <see cref="Status"/> whose <see cref="StatusCode"/> is
    /// resolved via <see cref="GrpcStatusCodeMap.Resolve"/> and whose detail is
    /// <see cref="SharedKernel.Primitives.Errors.Error.Message"/>.
    /// </exception>
    public static T ToGrpcResult<T>(this Result<T> result)
    {
        if (result.IsFailure)
        {
            throw BuildRpcException(result.Error);
        }

        return result.Value;
    }

    private static RpcException BuildRpcException(SharedKernel.Primitives.Errors.Error error)
    {
        var statusCode = GrpcStatusCodeMap.Resolve(error.Type);
        var status = new Status(statusCode, error.Message);
        return new RpcException(status);
    }
}
