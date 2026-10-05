using Google.Rpc;
using Grpc.Core;
using SharedKernel.Communication.Grpc.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using RpcStatus = Google.Rpc.Status;

namespace SharedKernel.Communication;

/// <summary>
/// Reads a gRPC call as a <see cref="Result"/>: the response, or the <see cref="Error"/> the service returned — rebuilt
/// from the rich status a platform service sends (<c>14.Presentation</c>'s <c>AddSharedKernelGrpc()</c>).
/// </summary>
/// <remarks>
/// <para>
/// The code is the status's <c>ErrorInfo</c> <c>reason</c> (the service's <see cref="Error.Code"/>), the message its
/// status message, and the <see cref="ErrorType"/> that of its status code. An <c>InvalidArgument</c> with a
/// <c>BadRequest</c> detail is <see cref="Error.Validation(IReadOnlyList{Error})"/> of its field violations, each keeping
/// its field as the <see cref="ErrorArgumentNames.PropertyPath"/> argument.
/// </para>
/// <para>
/// A status with no <c>ErrorInfo</c> — from a service outside the platform, or raised by the client — gets
/// <c>grpc.{status}</c>, except that a call that never reached the service is
/// <see cref="CommunicationErrorCodes.Unreachable"/>, one that ran out of its deadline
/// <see cref="CommunicationErrorCodes.Timeout"/>, and one whose access token could not be had
/// <see cref="CommunicationErrorCodes.AccessTokenUnavailable"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Result&lt;StockReply&gt; stock = await client.GetStockAsync(new StockRequest { Sku = sku }, cancellationToken: ct).ToResultAsync(ct);
/// </code>
/// </example>
public static class GrpcResultExtensions
{
    /// <summary>Awaits a unary call; its response, or the error it failed with. The call is disposed.</summary>
    /// <typeparam name="TResponse">The response message.</typeparam>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">
    /// The token the call was started with: when it is cancelled, the cancellation is thrown rather than returned.
    /// </param>
    /// <returns>The response, or the error.</returns>
    public static async Task<Result<TResponse>> ToResultAsync<TResponse>(
        this AsyncUnaryCall<TResponse> call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        using (call)
        {
            try
            {
                return Result<TResponse>.Success(await call.ResponseAsync.ConfigureAwait(false));
            }
            catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Result<TResponse>.Failure(exception.ToError());
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
            {
                throw new OperationCanceledException(exception.Status.Detail, exception, cancellationToken);
            }
        }
    }

    /// <summary>Rebuilds the <see cref="Error"/> a failed call carries.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The error.</returns>
    public static Error ToError(this RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (FindInner<AccessTokenUnavailableException>(exception.Status.DebugException) is { } tokenFailure)
        {
            return tokenFailure.Error;
        }

        // Raised by the client when the request never got a response: refused, reset, unresolvable, or TLS failed.
        if (FindInner<HttpRequestException>(exception.Status.DebugException) is not null)
        {
            return Error.Unavailable(CommunicationErrorCodes.Unreachable, "The service could not be reached.");
        }

        StatusCode statusCode = exception.StatusCode;
        RpcStatus? status = TryGetRpcStatus(exception);
        ErrorInfo? info = status?.GetDetail<ErrorInfo>();
        string message = FirstNonBlank(status?.Message, exception.Status.Detail) ?? $"The call failed with gRPC status {statusCode}.";

        if (statusCode == StatusCode.InvalidArgument
            && status?.GetDetail<BadRequest>() is { FieldViolations.Count: > 0 } badRequest)
        {
            return Error.Validation(badRequest.FieldViolations.Select(ToDetail).ToList());
        }

        string code = info is { Reason: { Length: > 0 } reason } ? reason : CodeWithoutErrorInfo(exception);
        return GrpcStatusErrorTypeMap.Create(GrpcStatusErrorTypeMap.Resolve(statusCode), code, message);
    }

    private static string CodeWithoutErrorInfo(RpcException exception) => exception.StatusCode switch
    {
        StatusCode.DeadlineExceeded => CommunicationErrorCodes.Timeout,
        _ => GrpcStatusErrorTypeMap.CodeFor(exception.StatusCode),
    };

    /// <summary>One field violation, the way the REST reader rebuilds a ProblemDetails field error.</summary>
    private static Error ToDetail(BadRequest.Types.FieldViolation violation)
    {
        string field = violation.Field;
        string code = string.IsNullOrWhiteSpace(violation.Reason) ? field : violation.Reason;
        Error error = Error.Validation(code, violation.Description);

        return string.IsNullOrEmpty(field) || string.Equals(field, code, StringComparison.Ordinal)
            ? error
            : error with
            {
                MessageArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [ErrorArgumentNames.PropertyPath] = field,
                },
            };
    }

    private static RpcStatus? TryGetRpcStatus(RpcException exception)
    {
        try
        {
            return exception.GetRpcStatus();
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            // A malformed grpc-status-details-bin trailer: read the status as if it had none.
            return null;
        }
    }

    private static string? FirstNonBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : null;

    private static T? FindInner<T>(Exception? exception)
        where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
