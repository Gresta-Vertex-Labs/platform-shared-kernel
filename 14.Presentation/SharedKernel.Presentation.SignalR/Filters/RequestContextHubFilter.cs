using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Carries the caller's <see cref="IRequestContext"/> — user, tenant and correlation id — from the request that opened
/// the connection into its connect handler, every hub method and its disconnect handler.
/// </summary>
/// <remarks>
/// <para>
/// A hub invocation does not run in the execution flow of the request that opened the connection, so the
/// <see cref="RequestContextScope"/> that <c>SharedKernel.ServiceDefaults.Security</c>'s
/// <c>UseSharedKernelRequestContext()</c> opened for that request is not ambient there. At connect time this filter
/// takes the ambient context (<see cref="IRequestContextAccessor.Current"/>, the connect request's scope) or, when
/// none is open, the <see cref="IRequestContext"/> registered in the connect request's services. It keeps that context
/// — whose caller <c>UseSharedKernelRequestContext()</c> fixes when its request ends, so it stays readable after a
/// long-polling request is gone — for the lifetime of the connection and opens a <see cref="RequestContextScope"/> with it around the connect handler,
/// every hub method and the disconnect handler, so hub code — and everything it calls: handlers, repositories,
/// outbound calls, log enrichment — reads the caller from <see cref="IRequestContext"/> or
/// <see cref="IRequestContextAccessor"/> like any other inbound adapter (WO-086 P-565, re-added by P-579).
/// </para>
/// <para>
/// Registered first by <c>AddSharedKernelSignalR()</c>, so it wraps the error mapping and the rate limit: their logs
/// carry the connection's correlation id. It never rejects a connection, with or without a context: whether a hub needs
/// a tenant is the hub's decision (<c>[RequireEndpointPermission]</c> and its siblings, or an explicit check).
/// </para>
/// </remarks>
internal sealed class RequestContextHubFilter : IHubFilter
{
    private static readonly object ConnectionContextKey = new();

    private readonly IRequestContextAccessor _accessor;

    /// <summary>Initializes a new instance of the <see cref="RequestContextHubFilter"/> class.</summary>
    /// <param name="accessor">Reads the ambient request context.</param>
    public RequestContextHubFilter(IRequestContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>Returns the request context the connection was opened with, or <see langword="null"/> when none was captured.</summary>
    /// <param name="context">The hub's <c>Context</c>.</param>
    /// <returns>The connection's request context.</returns>
    public static IRequestContext? GetConnectionContext(HubCallerContext context) =>
        context.Items.TryGetValue(ConnectionContextKey, out var value) ? value as IRequestContext : null;

    /// <summary>Captures the connecting caller's request context and runs the rest of the connect pipeline inside it.</summary>
    /// <param name="context">The hub lifetime context for the connecting client.</param>
    /// <param name="next">The next delegate in the connect pipeline.</param>
    /// <returns>A task that completes when the connect pipeline has finished.</returns>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var requestContext = _accessor.Current
            ?? context.Context.GetHttpContext()?.RequestServices?.GetService<IRequestContext>();

        if (requestContext is null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Context.Items[ConnectionContextKey] = requestContext;
        using (RequestContextScope.Begin(requestContext))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    /// <summary>Runs a hub method inside the connection's request context.</summary>
    /// <param name="invocationContext">The hub invocation context.</param>
    /// <param name="next">The next delegate in the invocation pipeline.</param>
    /// <returns>The hub method's result.</returns>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        if (GetConnectionContext(invocationContext.Context) is not { } requestContext)
        {
            return await next(invocationContext).ConfigureAwait(false);
        }

        using (RequestContextScope.Begin(requestContext))
        {
            return await next(invocationContext).ConfigureAwait(false);
        }
    }

    /// <summary>Runs the disconnect pipeline inside the connection's request context.</summary>
    /// <param name="context">The hub lifetime context for the disconnecting client.</param>
    /// <param name="exception">The exception that closed the connection, if any.</param>
    /// <param name="next">The next delegate in the disconnect pipeline.</param>
    /// <returns>A task that completes when the disconnect pipeline has finished.</returns>
    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (GetConnectionContext(context.Context) is not { } requestContext)
        {
            await next(context, exception).ConfigureAwait(false);
            return;
        }

        using (RequestContextScope.Begin(requestContext))
        {
            await next(context, exception).ConfigureAwait(false);
        }
    }
}
