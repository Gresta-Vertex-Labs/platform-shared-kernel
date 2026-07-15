using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Injects W3C <c>traceparent</c>, <c>tracestate</c>, and <c>x-correlation-id</c> metadata
/// into every outgoing gRPC call.
/// Reads <see cref="Activity.Current"/> at the moment of the call (not at DI registration time).
/// Never overwrites a caller-supplied <c>x-correlation-id</c> metadata entry.
/// Catches all exceptions, logs at <see cref="LogLevel.Error"/>, and continues — never propagates
/// into the gRPC call pipeline.
/// </summary>
internal sealed partial class CorrelationTracingInterceptor(ILogger<CorrelationTracingInterceptor> logger) : Interceptor
{
    private readonly ILogger<CorrelationTracingInterceptor> _logger = logger;

    // Thin value-forwarding alias of 01.Core's WellKnownHeaders.CorrelationId (P-259/P-260) — retained
    // as a locally-named const because gRPC metadata keys are conventionally lowercase and the local
    // symbol name reads more naturally at gRPC call sites; Grpc.Core.Metadata normalizes key casing
    // internally, so no behavioral change results from sourcing the uppercase-hyphenated literal here.
    internal const string CorrelationIdKey = WellKnownHeaders.CorrelationId;
    internal const string TraceParentKey = "traceparent";
    internal const string TraceStateKey = "tracestate";

    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(context);
    }

    /// <inheritdoc />
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(context);
    }

    private ClientInterceptorContext<TRequest, TResponse> EnrichContext<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            var headers = context.Options.Headers ?? new Metadata();

            // Read Activity.Current at call time (never at DI registration time)
            var activity = Activity.Current;

            // Inject traceparent in W3C format: 00-{traceId}-{spanId}-{flags}
            if (activity is not null && !GrpcMetadataHelper.HasMetadataEntry(headers, TraceParentKey))
            {
                var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00")}";
                headers = GrpcMetadataHelper.CloneAndAdd(headers, TraceParentKey, traceParent);
            }

            // Inject tracestate when present
            if (activity is not null && !string.IsNullOrEmpty(activity.TraceStateString)
                && !GrpcMetadataHelper.HasMetadataEntry(headers, TraceStateKey))
            {
                headers = GrpcMetadataHelper.CloneAndAdd(headers, TraceStateKey, activity.TraceStateString);
            }

            // Inject x-correlation-id — do not overwrite if caller set it
            if (!GrpcMetadataHelper.HasMetadataEntry(headers, CorrelationIdKey))
            {
                var correlationId = activity?.Id ?? Guid.NewGuid().ToString("N");
                headers = GrpcMetadataHelper.CloneAndAdd(headers, CorrelationIdKey, correlationId);
            }

            var newOptions = context.Options.WithHeaders(headers);
            return new ClientInterceptorContext<TRequest, TResponse>(
                context.Method, context.Host, newOptions);
        }
        catch (Exception ex)
        {
            LogCorrelationEnrichmentFailed(_logger, ex);
            return context;
        }
    }

    [LoggerMessage(
        EventId = 11100,
        Level = LogLevel.Error,
        Message = "CorrelationTracingInterceptor failed to enrich gRPC metadata. Continuing without propagation.")]
    private static partial void LogCorrelationEnrichmentFailed(ILogger logger, Exception exception);
}
