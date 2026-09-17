using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.Grpc.Interceptors;

/// <summary>
/// Global server interceptor that attaches the resolved tenant identifier to every gRPC call —
/// the gRPC counterpart to
/// <c>SharedKernel.Presentation.SignalR.Filters.TenantContextHubFilter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="ITenantProvider"/> (<c>12.Security.Abstractions</c>) from the call's
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> — obtained via
/// <see cref="ServerCallContextExtensions.GetHttpContext"/>, ASP.NET Core gRPC hosting's
/// documented bridge from <see cref="ServerCallContext"/> to the underlying
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> — and stores the resolved
/// <see cref="ITenantProvider.TenantId"/> in <see cref="ServerCallContext.UserState"/> under
/// <see cref="ItemsKey"/> for the call's lifetime, the gRPC per-call analogue of
/// <c>Context.Items</c>/<c>HttpContext.Items</c>.
/// </para>
/// <para>
/// Does not reject calls with no resolvable tenant — mirrors
/// <c>TenantContextHubFilter</c>'s non-rejecting policy exactly; that decision belongs to the
/// consuming service's own gRPC service implementation or a stacked
/// <c>GrpcAuthorizationInterceptor</c> attribute, never this interceptor.
/// </para>
/// <para>
/// Overrides all four server interceptor methods, since gRPC has three streaming call shapes in
/// addition to unary that equally need tenant-context attachment. Unlike SignalR's
/// connection-scoped <c>OnConnectedAsync</c>, gRPC has no persistent per-connection hook at this
/// abstraction level — each call (unary or streaming) gets its own <see cref="ServerCallContext"/>
/// and is enriched independently.
/// </para>
/// </remarks>
public sealed class GrpcTenantContextInterceptor : Interceptor
{
    /// <summary>The <see cref="ServerCallContext.UserState"/> key the resolved tenant identifier is stored under.</summary>
    public const string ItemsKey = "TenantId";

    /// <inheritdoc />
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(request, context);
    }

    /// <inheritdoc />
    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(requestStream, context);
    }

    /// <inheritdoc />
    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(request, responseStream, context);
    }

    /// <inheritdoc />
    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Enrich(context);
        return continuation(requestStream, responseStream, context);
    }

    private static void Enrich(ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var tenantProvider = httpContext?.RequestServices.GetService<ITenantProvider>();

        context.UserState[ItemsKey] = tenantProvider?.TenantId ?? Guid.Empty;
    }
}
