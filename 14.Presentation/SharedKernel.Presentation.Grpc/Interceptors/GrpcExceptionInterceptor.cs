using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// The server interceptor registered by <c>AddSharedKernelGrpc()</c>: turns every exception a gRPC service method
/// throws into an <see cref="RpcException"/> with the platform's rich status — the gRPC counterpart of the HTTP
/// exception handler, for all four call shapes.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Any exception once the call is cancelled — the client cancelled it, or its deadline passed — ends as
///   <see cref="StatusCode.Cancelled"/>, logged at Debug, whatever its type: an <see cref="OperationCanceledException"/>,
///   the <see cref="IOException"/> of an aborted stream, the <see cref="InvalidOperationException"/> of a write to a
///   completed call, the failure of a downstream call cancelled with it. Nobody is left to read the answer, and it is
///   not a failure of the service.</item>
///   <item>An <see cref="RpcException"/> — thrown by the service's own code, or by a gRPC call it made to another
///   service — is rebuilt without any of its trailers, so another service's <c>ErrorInfo</c>, field violations, trace
///   ids and error domain never reach this service's caller. Its status code is kept, with this service's
///   <c>ErrorInfo</c>: <c>reason</c> = <see cref="GrpcErrorCodes.ForStatus"/>, this error domain, this call's trace
///   and correlation ids. Its detail is kept for a client category; for a server category
///   (<see cref="StatusCode.Unknown"/>, <see cref="StatusCode.Internal"/>, <see cref="StatusCode.DataLoss"/>,
///   <see cref="StatusCode.Unavailable"/>, <see cref="StatusCode.DeadlineExceeded"/>) it is replaced outside
///   Development by the generic sentence of the matching error type. A thrown <see cref="StatusCode.OK"/> is no
///   answer and becomes <see cref="StatusCode.Unknown"/>. A service sends trailers of its own through
///   <see cref="ServerCallContext.ResponseTrailers"/>.</item>
///   <item><see cref="ValidationException"/>: <see cref="StatusCode.InvalidArgument"/> with exactly the status a
///   returned error of the same field errors gets, as over HTTP — one error is itself, several are
///   <c>Error.Validation(errors)</c>, whose field errors become the field violations.</item>
///   <item>Any other <see cref="SharedKernelException"/>: its <see cref="Error"/>. This is also how a failed result
///   arrives: <c>SharedKernel.Core</c>'s <c>GetValueOrThrow()</c> and <c>ThrowIfFailure()</c> throw
///   <c>Error.ToException()</c> — the exception of the error's type, a <see cref="DomainException"/> for
///   <see cref="ErrorType.Unavailable"/> and <see cref="ErrorType.Timeout"/> — which carries the error unchanged.</item>
///   <item>A <see cref="TimeoutException"/>, or an <see cref="OperationCanceledException"/> while the call is not
///   cancelled (a timeout inside the service): <see cref="StatusCode.DeadlineExceeded"/> <c>timeout.default</c>, the
///   status a returned <see cref="Error.Timeout"/> gets — as over HTTP, which answers these with 504.</item>
///   <item>Anything else: <see cref="StatusCode.Internal"/> <c>unexpected.exception</c> with a generic message; in
///   Development the exception message instead.</item>
/// </list>
/// Server categories are logged at Error with the exception, client categories at Debug. The status message is the
/// one an HTTP client would get (translated, server errors redacted outside Development), and nothing about the
/// exception reaches the client outside Development.
/// </remarks>
internal sealed partial class GrpcExceptionInterceptor : Interceptor
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private const string TimeoutMessage = "The operation did not complete in time.";

    private const string CancelledMessage = "The call was cancelled.";

    private readonly ILogger<GrpcExceptionInterceptor> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<SharedKernelGrpcOptions> _options;

    public GrpcExceptionInterceptor(
        ILogger<GrpcExceptionInterceptor> logger,
        IHostEnvironment environment,
        IOptions<SharedKernelGrpcOptions> options)
    {
        _logger = logger;
        _environment = environment;
        _options = options;
    }

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Map(exception, context);
        }
    }

    /// <inheritdoc />
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(requestStream, context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Map(exception, context);
        }
    }

    /// <inheritdoc />
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Map(exception, context);
        }
    }

    /// <inheritdoc />
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(requestStream, responseStream, context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw Map(exception, context);
        }
    }

    // Every exception is mapped: this interceptor is the outermost, so the only RpcException it ever sees is one it
    // did not build — the service's own, or another service's.
    private RpcException Map(Exception exception, ServerCallContext context)
    {
        if (context.CancellationToken.IsCancellationRequested)
        {
            Log.CallCancelled(_logger, context.Method, exception);
            return new RpcException(new Status(StatusCode.Cancelled, CancelledMessage));
        }

        var httpContext = FindHttpContext(context);
        var domain = _options.Value.ErrorDomain;

        switch (exception)
        {
            case RpcException rpcException:
                return Rebuild(rpcException, context, httpContext, domain);

            case ValidationException validation:
                // Exactly what a returned error produces, as over HTTP: one error is itself, several are
                // Error.Validation(errors); the field violations are the error's Details.
                var error = validation.Errors.Count == 1 ? validation.Errors[0] : Error.Validation(validation.Errors);
                Log.ClientError(_logger, context.Method, StatusCode.InvalidArgument, error.Code, exception);
                return RpcStatusFactory.CreateException(error, httpContext, domain, StatusCode.InvalidArgument);

            case SharedKernelException sharedKernelException:
                LogFailure(context, sharedKernelException.Error, exception);
                return RpcStatusFactory.CreateException(sharedKernelException.Error, httpContext, domain);

            case TimeoutException:
            case OperationCanceledException:
                // Not the client: the call is still open, so something inside the service ran out of time. The status
                // of a returned Error.Timeout, as HTTP answers the same exception with 504 timeout.default.
                var timeout = Error.Timeout(ErrorCodes.Timeout.Default, TimeoutMessage);
                LogFailure(context, timeout, exception);
                return RpcStatusFactory.CreateException(timeout, httpContext, domain);

            default:
                Log.UnhandledException(_logger, context.Method, exception);
                return RpcStatusFactory.CreateException(
                    Error.Unexpected(ErrorCodes.Unexpected.Default, UnexpectedMessage),
                    httpContext,
                    domain,
                    clientMessage: _environment.IsDevelopment() ? exception.Message : null);
        }
    }

    // The status of an RpcException this service did not build from an error, rebuilt as its own: the code, the
    // detail where it may be shown, this service's ErrorInfo — and nothing from the original trailers.
    private RpcException Rebuild(RpcException exception, ServerCallContext context, HttpContext? httpContext, string domain)
    {
        var statusCode = exception.StatusCode == StatusCode.OK ? StatusCode.Unknown : exception.StatusCode;
        var code = GrpcErrorCodes.ForStatus(statusCode);
        var detail = exception.Status.Detail;

        if (ServerErrorType(statusCode) is not { } serverErrorType)
        {
            Log.ClientError(_logger, context.Method, statusCode, code, exception);
            return RpcStatusFactory.CreateExceptionForStatus(statusCode, detail, httpContext, domain);
        }

        Log.ServerError(_logger, context.Method, statusCode, code, exception);

        // Presented like an error of the same category: the generic sentence outside Development, the detail in it.
        var message = ErrorPresentation.GetClientMessage(new Error(code, detail, serverErrorType), httpContext);
        return RpcStatusFactory.CreateExceptionForStatus(statusCode, message, httpContext, domain);
    }

    // The error type a status of a server category is presented as; null for a client category, whose detail is the
    // service's (or another service's) deliberate answer and is shown.
    private static ErrorType? ServerErrorType(StatusCode statusCode) => statusCode switch
    {
        StatusCode.Unknown or StatusCode.Internal or StatusCode.DataLoss => ErrorType.Unexpected,
        StatusCode.Unavailable => ErrorType.Unavailable,
        StatusCode.DeadlineExceeded => ErrorType.Timeout,
        _ => null,
    };

    private void LogFailure(ServerCallContext context, Error error, Exception exception)
    {
        var statusCode = GrpcStatusCodeMap.Resolve(error.Type);

        if (ErrorPresentation.IsServerError(error.Type))
        {
            Log.ServerError(_logger, context.Method, statusCode, error.Code, exception);
        }
        else
        {
            Log.ClientError(_logger, context.Method, statusCode, error.Code, exception);
        }
    }

    // A ServerCallContext built by hand (a unit test) has no request: messages are then untranslated and server errors
    // redacted, as in production.
    private static HttpContext? FindHttpContext(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 200,
            Level = LogLevel.Error,
            Message = "Unhandled exception in gRPC method {GrpcMethod}.")]
        public static partial void UnhandledException(ILogger logger, string grpcMethod, Exception exception);

        // 14201 was the refusal log of the deleted authorization interceptor; refusals are logged by
        // SharedKernel.Presentation.Core's authorization (14002) now. Not reused.
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 202,
            Level = LogLevel.Error,
            Message = "gRPC method {GrpcMethod} failed with {StatusCode} {ErrorCode}.")]
        public static partial void ServerError(ILogger logger, string grpcMethod, StatusCode statusCode, string errorCode, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 203,
            Level = LogLevel.Debug,
            Message = "gRPC method {GrpcMethod} was rejected with {StatusCode} {ErrorCode}.")]
        public static partial void ClientError(ILogger logger, string grpcMethod, StatusCode statusCode, string errorCode, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 204,
            Level = LogLevel.Debug,
            Message = "The call to gRPC method {GrpcMethod} was cancelled before it completed.")]
        public static partial void CallCancelled(ILogger logger, string grpcMethod, Exception exception);
    }
}
