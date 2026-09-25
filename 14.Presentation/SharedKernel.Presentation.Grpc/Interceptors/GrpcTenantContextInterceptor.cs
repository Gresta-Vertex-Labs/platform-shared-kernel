using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// Global server interceptor that runs every gRPC call inside the caller's <see cref="IRequestContext"/> — user and
/// tenant — so the service method reads it through <see cref="IRequestContextAccessor"/>. The gRPC counterpart to
/// <c>SharedKernel.Presentation.SignalR.Filters.TenantContextHubFilter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Takes the ambient context (<see cref="IRequestContextAccessor.Current"/>, set by an inbound adapter such as
/// <c>SharedKernel.MultiTenancy</c>'s tenant resolution) or, when none is open, the <see cref="IRequestContext"/>
/// registered in the call's <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestServices"/> — obtained via
/// <see cref="ServerCallContextExtensions.GetHttpContext"/>, ASP.NET Core gRPC hosting's documented bridge from
/// <see cref="ServerCallContext"/> to the underlying <see cref="Microsoft.AspNetCore.Http.HttpContext"/> — and opens a
/// <see cref="RequestContextScope"/> with it for the duration of the call. When neither exists, the call runs as
/// <see cref="AnonymousRequestContext"/>.
/// </para>
/// <para>
/// The scope always carries the call's correlation id, as resolved by <see cref="GrpcCorrelationInterceptor"/>
/// (the ambient one, else the caller's metadata, else a new one), so an outbound REST or gRPC call, message or
/// workflow started by the service method forwards the caller's id unchanged.
/// </para>
/// <para>
/// Does not reject calls with no resolvable tenant — mirrors
/// <c>TenantContextHubFilter</c>'s non-rejecting policy exactly; that decision belongs to the
/// consuming service's own gRPC service implementation or a stacked
/// <c>GrpcAuthorizationInterceptor</c> attribute, never this interceptor.
/// </para>
/// <para>
/// Overrides all four server interceptor methods, since gRPC has three streaming call shapes in
/// addition to unary that equally need the context. Each call (unary or streaming) gets its own
/// <see cref="ServerCallContext"/> and its own scope.
/// </para>
/// </remarks>
public sealed class GrpcTenantContextInterceptor : Interceptor
{
    private readonly IRequestContextAccessor _accessor;

    /// <summary>Initialises a new <see cref="GrpcTenantContextInterceptor"/>.</summary>
    /// <param name="accessor">Reads the ambient request context.</param>
    public GrpcTenantContextInterceptor(IRequestContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = Begin(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = Begin(context);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = Begin(context);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = Begin(context);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }

    private IDisposable Begin(ServerCallContext context)
    {
        var requestContext = _accessor.Current
            ?? context.GetHttpContext()?.RequestServices.GetService<IRequestContext>()
            ?? AnonymousRequestContext.Instance;

        var correlationId = GrpcCorrelationInterceptor.Resolve(context);
        if (!string.Equals(requestContext.CorrelationId, correlationId, StringComparison.Ordinal))
            requestContext = requestContext.WithCorrelationId(correlationId);

        return RequestContextScope.Begin(requestContext);
    }
}
