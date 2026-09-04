using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// Global server interceptor that converts unhandled exceptions thrown by a gRPC service method
/// into safe <see cref="RpcException"/> instances — the gRPC counterpart to
/// <c>SharedKernel.Presentation.WebApi.ExceptionHandling.SharedKernelExceptionHandler</c> /
/// <c>SharedKernel.Presentation.SignalR.Filters.HubExceptionMappingFilter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Overrides all four server interceptor methods — <see cref="UnaryServerHandler{TRequest,TResponse}"/>,
/// <see cref="ClientStreamingServerHandler{TRequest,TResponse}"/>,
/// <see cref="ServerStreamingServerHandler{TRequest,TResponse}"/>,
/// <see cref="DuplexStreamingServerHandler{TRequest,TResponse}"/> — never unary-only, since gRPC
/// (unlike single-request/response HTTP) has three streaming call shapes that equally need
/// exception mapping.
/// </para>
/// <para>
/// Known <see cref="SharedKernelException"/> subtypes (<c>01.Core</c>) — including
/// <see cref="ValidationException"/>, mapped via its inherited <c>Error</c> property — are
/// rethrown as <see cref="RpcException"/> using <see cref="GrpcStatusCodeMap.Resolve"/> and
/// <see cref="SharedKernel.Primitives.Errors.Error.Message"/> as the status detail — caller-safe,
/// no stack trace, no internal type names. Unknown exceptions are logged at
/// <see cref="LogLevel.Error"/> and rethrown as a generic <see cref="RpcException"/> with detail
/// suppressed outside <see cref="IHostEnvironment.IsDevelopment"/>. An already-thrown
/// <see cref="RpcException"/> is passed through unchanged. A non-<see cref="RpcException"/> must
/// never cross this interceptor boundary.
/// </para>
/// </remarks>
public sealed partial class GrpcExceptionInterceptor : Interceptor
{
    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private readonly ILogger<GrpcExceptionInterceptor> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Initialises a new <see cref="GrpcExceptionInterceptor"/>.
    /// </summary>
    /// <param name="logger">The logger used to record unknown exceptions before redaction.</param>
    /// <param name="environment">
    /// The hosting environment, used to gate unknown-exception detail exposure to
    /// <see cref="IHostEnvironment.IsDevelopment"/> only.
    /// </param>
    public GrpcExceptionInterceptor(ILogger<GrpcExceptionInterceptor> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
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
            throw MapException(exception, context.Method);
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
            throw MapException(exception, context.Method);
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
            throw MapException(exception, context.Method);
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
            throw MapException(exception, context.Method);
        }
    }

    private RpcException MapException(Exception exception, string methodName)
    {
        switch (exception)
        {
            case RpcException rpcException:
                // Already a safe, client-facing exception — pass it through unchanged.
                return rpcException;

            case SharedKernelException sharedKernelException:
                var statusCode = GrpcStatusCodeMap.Resolve(sharedKernelException.Error.Type);
                return new RpcException(new Status(statusCode, sharedKernelException.Error.Message));

            default:
                Log.UnhandledGrpcException(_logger, methodName, exception);
                var detail = _environment.IsDevelopment() ? exception.Message : UnexpectedErrorMessage;
                return new RpcException(new Status(StatusCode.Internal, detail));
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="GrpcExceptionInterceptor"/>.
    /// </summary>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 200,
            Level = LogLevel.Error,
            Message = "Unhandled exception in gRPC method {GrpcMethod}.")]
        public static partial void UnhandledGrpcException(ILogger logger, string grpcMethod, Exception exception);
    }
}
