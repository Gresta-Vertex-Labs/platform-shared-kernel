using Grpc.Core;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.Grpc;

/// <summary>
/// Ends a gRPC service method with the outcome of a <see cref="Result"/> or <see cref="Result{T}"/>: nothing or the
/// value on success, an <see cref="RpcException"/> with the platform's rich status on failure.
/// </summary>
/// <remarks>
/// <para>
/// A failure throws the status the exception interceptor of <c>AddSharedKernelGrpc()</c> gives the same error when
/// it is thrown: the code from <see cref="GrpcStatusCodeMap"/>, the client message (translated, server errors
/// redacted outside Development), an <c>ErrorInfo</c> detail with the error code as <c>reason</c>, the error domain,
/// the trace id and the correlation id, and a <c>BadRequest</c> detail with one field violation per entry of
/// <see cref="SharedKernel.Primitives.Errors.Error.Details"/>. The interceptor completes the status with the call's
/// request and domain; called outside a call, as in a unit test of the service class, the status has neither, is
/// untranslated, and redacts server errors as in production.
/// </para>
/// <para>
/// Every method also exists for <see cref="Task{TResult}"/>, so a method can end with
/// <c>return Map(await sender.Send(query, context.CancellationToken).GetValueOrThrow());</c>. Never build an
/// <see cref="RpcException"/> or <see cref="Status"/> by hand for a failed result (SK0036), and never branch on
/// <c>IsSuccess</c> to do so.
/// </para>
/// </remarks>
public static class GrpcResultExtensions
{
    /// <summary>Throws when <paramref name="result"/> failed; does nothing on success.</summary>
    /// <param name="result">The result.</param>
    /// <exception cref="RpcException">The result failed; the exception carries its error as a rich status.</exception>
    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure)
        {
            throw ResultFailures.CreateException(result.Error);
        }
    }

    /// <summary>Returns the value of <paramref name="result"/>, or throws when it failed.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The value.</returns>
    /// <exception cref="RpcException">The result failed; the exception carries its error as a rich status.</exception>
    public static T GetValueOrThrow<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            throw ResultFailures.CreateException(result.Error);
        }

        return result.Value;
    }

    /// <inheritdoc cref="ThrowIfFailure(Result)"/>
    public static async Task ThrowIfFailure(this Task<Result> result) =>
        (await Await(result).ConfigureAwait(false)).ThrowIfFailure();

    /// <inheritdoc cref="GetValueOrThrow{T}(Result{T})"/>
    public static async Task<T> GetValueOrThrow<T>(this Task<Result<T>> result) =>
        (await Await(result).ConfigureAwait(false)).GetValueOrThrow();

    private static Task<TResult> Await<TResult>(Task<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task;
    }
}
