using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Injects the ambient caller's tenant, actor and client metadata (<c>X-Tenant-Id</c>, <c>x-sk-actor-id</c>,
/// <c>x-sk-actor-kind</c>, <c>x-sk-client-id</c>) into every outgoing gRPC call.
/// </summary>
/// <remarks>
/// <para>
/// The caller is <see cref="IRequestContextAccessor.Current"/>, the context every inbound adapter makes ambient —
/// the HTTP request-context middleware, the gRPC server interceptor, the message consume filter, the workflow
/// activity interceptor and the scheduler. No <c>IHttpContextAccessor</c> is involved (P-566), so a call made from
/// a message consumer or a background job carries its caller exactly like one made from an HTTP request. The
/// correlation id is written by <see cref="CorrelationTracingInterceptor"/>.
/// </para>
/// <para>
/// Silent no-op when no context is open. Never overwrites a caller-supplied metadata entry. Catches all exceptions,
/// logs at <see cref="LogLevel.Error"/>, and continues.
/// </para>
/// </remarks>
internal sealed partial class TenantIdInterceptor(
    IRequestContextAccessor accessor,
    ILogger<TenantIdInterceptor> logger) : Interceptor
{
    private readonly IRequestContextAccessor _accessor = accessor;
    private readonly ILogger<TenantIdInterceptor> _logger = logger;

    // Thin value-forwarding alias of 01.Core's WellKnownHeaders.TenantId (P-259/P-260); Grpc.Core.Metadata
    // normalizes key casing internally, so the uppercase-hyphenated literal behaves identically.
    internal const string TenantIdKey = WellKnownHeaders.TenantId;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(request, context);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(request, context);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        context = EnrichContext(context);
        return continuation(context);
    }

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
            if (_accessor.Current is not { } caller)
                return context;

            var headers = context.Options.Headers ?? new Metadata();
            var added = false;

            // The correlation id is CorrelationTracingInterceptor's; this interceptor writes the caller only.
            RequestContextPropagation.WriteHeaders(caller, headers, (_, name, value) =>
            {
                if (name == WellKnownHeaders.CorrelationId || GrpcMetadataHelper.HasMetadataEntry(headers, name))
                    return;

                headers = GrpcMetadataHelper.CloneAndAdd(headers, name, value);
                added = true;
            });

            if (!added)
                return context;

            var newOptions = context.Options.WithHeaders(headers);
            return new ClientInterceptorContext<TRequest, TResponse>(
                context.Method, context.Host, newOptions);
        }
        catch (Exception ex)
        {
            LogTenantIdEnrichmentFailed(_logger, ex);
            return context;
        }
    }

    [LoggerMessage(
        EventId = 11101,
        Level = LogLevel.Error,
        Message = "TenantIdInterceptor failed to inject x-tenant-id metadata. Continuing without tenant propagation.")]
    private static partial void LogTenantIdEnrichmentFailed(ILogger logger, Exception exception);
}
