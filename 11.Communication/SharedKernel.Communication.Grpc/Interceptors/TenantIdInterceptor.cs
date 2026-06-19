using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Injects <c>x-tenant-id</c> metadata into every outgoing gRPC call by resolving
/// <c>ITenantProvider</c> from the current request scope via <see cref="IHttpContextAccessor"/>.
/// Silent no-op when <see cref="IHttpContextAccessor.HttpContext"/> is null,
/// when <c>ITenantProvider</c> is not registered, or when <c>TenantId</c> is <see cref="Guid.Empty"/>.
/// Catches all exceptions, logs at <see cref="LogLevel.Error"/>, and continues — never propagates.
/// </summary>
internal sealed class TenantIdInterceptor(
    IHttpContextAccessor httpContextAccessor,
    ILogger<TenantIdInterceptor> logger) : Interceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<TenantIdInterceptor> _logger = logger;

    internal const string TenantIdKey = "x-tenant-id";

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

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
                return context;

            var tenantProvider = httpContext.RequestServices.GetService<ITenantProvider>();
            if (tenantProvider is null || tenantProvider.TenantId == Guid.Empty)
                return context;

            headers = GrpcMetadataHelper.CloneAndAdd(headers, TenantIdKey, tenantProvider.TenantId.ToString());

            var newOptions = context.Options.WithHeaders(headers);
            return new ClientInterceptorContext<TRequest, TResponse>(
                context.Method, context.Host, newOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TenantIdInterceptor failed to inject x-tenant-id metadata. Continuing without tenant propagation.");
            return context;
        }
    }
}
