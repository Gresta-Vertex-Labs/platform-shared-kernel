using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Injects W3C <c>traceparent</c>, <c>tracestate</c>, and <c>x-correlation-id</c> metadata
/// into every outgoing gRPC call.
/// Reads <see cref="System.Diagnostics.Activity.Current"/> at the moment of the call (not at DI registration time).
/// Never overwrites a caller-supplied <c>x-correlation-id</c> metadata entry.
/// Catches all exceptions, logs at <see cref="LogLevel.Error"/>, and continues — never propagates
/// into the gRPC call pipeline.
/// </summary>
internal sealed class CorrelationTracingInterceptor(ILogger<CorrelationTracingInterceptor> logger) : Interceptor
{
    private readonly ILogger<CorrelationTracingInterceptor> _logger = logger;
    internal const string CorrelationIdKey = "x-correlation-id";
    internal const string TraceParentKey = "traceparent";
    internal const string TraceStateKey = "tracestate";

    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        // TODO: implement
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
