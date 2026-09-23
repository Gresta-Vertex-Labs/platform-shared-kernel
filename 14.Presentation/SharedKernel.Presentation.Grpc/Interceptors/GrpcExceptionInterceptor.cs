using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Options;
using SharedKernel.Presentation.WebApi.Errors;
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
///   <item>An <see cref="RpcException"/> the service built itself passes through unchanged.</item>
///   <item>A failed result thrown by <see cref="GrpcResultExtensions"/> gets its status rebuilt with the call's
///   request and error domain. It is an expected outcome and is not logged, like a result returned over HTTP.</item>
///   <item>An <see cref="OperationCanceledException"/> after the client cancelled the call (or its deadline passed)
///   ends as <see cref="StatusCode.Cancelled"/>, logged at Debug: nobody is left to read the answer, and it is not a
///   failure of the service.</item>
///   <item><see cref="ValidationException"/>: <see cref="StatusCode.InvalidArgument"/> with every field error as a
///   field violation.</item>
///   <item>Any other <see cref="SharedKernelException"/>: its <see cref="Error"/>.</item>
///   <item>Anything else: <see cref="StatusCode.Internal"/> <c>unexpected.exception</c> with a generic message; in
///   Development the exception message instead.</item>
/// </list>
/// Server errors (<see cref="ErrorPresentation.IsServerError"/>) are logged at Error with the exception, client
/// errors at Debug. The status message is the one an HTTP client would get (translated, server errors redacted
/// outside Development), and nothing about the exception reaches the client outside Development.
/// </remarks>
internal sealed partial class GrpcExceptionInterceptor : Interceptor
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

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
        catch (Exception exception) when (IsMapped(exception))
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
        catch (Exception exception) when (IsMapped(exception))
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
        catch (Exception exception) when (IsMapped(exception))
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
        catch (Exception exception) when (IsMapped(exception))
        {
            throw Map(exception, context);
        }
    }

    // An RpcException the service built itself is its deliberate answer and keeps its stack trace: it is never caught.
    private static bool IsMapped(Exception exception) =>
        exception is not RpcException rpcException || ResultFailures.TryGetError(rpcException, out _);

    private RpcException Map(Exception exception, ServerCallContext context)
    {
        var httpContext = FindHttpContext(context);
        var domain = _options.Value.ErrorDomain;

        switch (exception)
        {
            case RpcException rpcException when ResultFailures.TryGetError(rpcException, out var resultError):
                return RpcStatusFactory.CreateException(resultError, httpContext, domain);

            case OperationCanceledException when context.CancellationToken.IsCancellationRequested:
                Log.CallCancelled(_logger, context.Method, exception);
                return new RpcException(new Status(StatusCode.Cancelled, CancelledMessage));

            case ValidationException validation:
                // One field error keeps its own code and message; several are reported like a result carrying
                // Error.Validation(errors). Either way every field error becomes a field violation — as over HTTP.
                var error = validation.Errors.Count == 1 ? validation.Errors[0] : Error.Validation(validation.Errors);
                var fieldErrors = error.Details.Count > 0 ? error.Details : validation.Errors;
                Log.ClientError(_logger, context.Method, StatusCode.InvalidArgument, error.Code, exception);
                return RpcStatusFactory.CreateException(error, httpContext, domain, fieldErrors, StatusCode.InvalidArgument);

            case SharedKernelException sharedKernelException:
                LogFailure(context, sharedKernelException.Error, exception);
                return RpcStatusFactory.CreateException(sharedKernelException.Error, httpContext, domain);

            default:
                Log.UnhandledException(_logger, context.Method, exception);
                return RpcStatusFactory.CreateException(
                    Error.Unexpected(ErrorCodes.Unexpected.Default, UnexpectedMessage),
                    httpContext,
                    domain,
                    clientMessage: _environment.IsDevelopment() ? exception.Message : null);
        }
    }

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

        // 14201 was the refusal log of the deleted authorization interceptor; refusals are logged by the WebApi core's
        // authorization (14002) now. Not reused.
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
            Message = "The client cancelled the call to gRPC method {GrpcMethod} before it completed.")]
        public static partial void CallCancelled(ILogger logger, string grpcMethod, Exception exception);
    }
}
