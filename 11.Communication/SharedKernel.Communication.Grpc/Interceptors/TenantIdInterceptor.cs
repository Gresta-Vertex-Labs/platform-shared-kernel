using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Grpc.Interceptors;

/// <summary>
/// Injects <c>x-tenant-id</c> metadata into every outgoing gRPC call by resolving
/// <c>IUserContext</c> from the current request scope via <see cref="IHttpContextAccessor"/>.
/// Silent no-op when <see cref="IHttpContextAccessor.HttpContext"/> is null,
/// when <c>IUserContext</c> is not registered, or when <c>TenantId</c> is null.
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
