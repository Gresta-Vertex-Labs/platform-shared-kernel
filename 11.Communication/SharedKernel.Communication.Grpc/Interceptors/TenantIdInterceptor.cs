using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Injects <c>x-tenant-id</c> metadata into every outgoing gRPC call by resolving
/// the tenant of the ambient <see cref="RequestContextScope.Current"/>, or else of the <see cref="IRequestContext"/>
/// registered in the current request scope (via <see cref="IHttpContextAccessor"/>).
/// Silent no-op when neither is available or the context has no tenant.
/// Catches all exceptions, logs at <see cref="LogLevel.Error"/>, and continues — never propagates.
/// </summary>
internal sealed partial class TenantIdInterceptor(
    IHttpContextAccessor httpContextAccessor,
    ILogger<TenantIdInterceptor> logger) : Interceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<TenantIdInterceptor> _logger = logger;

    // Thin value-forwarding alias of 01.Core's WellKnownHeaders.TenantId (P-259/P-260) — retained as a
    // locally-named const because gRPC metadata keys are conventionally lowercase and the local symbol
    // name reads more naturally at gRPC call sites; Grpc.Core.Metadata normalizes key casing internally,
    // so no behavioral change results from sourcing the uppercase-hyphenated literal here.
    internal const string TenantIdKey = WellKnownHeaders.TenantId;

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

            // Do not overwrite a caller-supplied x-tenant-id entry
            if (GrpcMetadataHelper.HasMetadataEntry(headers, TenantIdKey))
                return context;

            var tenantId = RequestContextScope.Current?.TenantId
                ?? _httpContextAccessor.HttpContext?.RequestServices.GetService<IRequestContext>()?.TenantId;
            if (tenantId is not { } tenant)
                return context;

            headers = GrpcMetadataHelper.CloneAndAdd(headers, TenantIdKey, tenant.ToString());

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
