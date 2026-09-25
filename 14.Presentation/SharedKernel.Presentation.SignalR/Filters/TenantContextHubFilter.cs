using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Carries the caller's <see cref="IRequestContext"/> — user and tenant — from the connect request into every hub
/// method invocation, where it is available through <see cref="IRequestContextAccessor"/>.
/// </summary>
/// <remarks>
/// At connect time the filter takes the ambient context (<see cref="IRequestContextAccessor.Current"/>, set by an
/// inbound adapter such as <c>SharedKernel.MultiTenancy</c>'s tenant resolution) or, when none is open, the
/// <see cref="IRequestContext"/> registered in the connect request's services. It keeps that context for the
/// lifetime of the connection and opens a <see cref="RequestContextScope"/> with it around the connect handler,
/// every hub method and the disconnect handler, so hub code reads the tenant from
/// <see cref="IRequestContextAccessor"/> like any other inbound adapter. Does not reject connections with no tenant —
/// that policy decision belongs to the consuming service's Hub (via
/// <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute"/> or explicit checks); this filter only
/// attaches data.
/// </remarks>
public sealed class TenantContextHubFilter : IHubFilter
{
    private static readonly object ConnectionContextKey = new();

    private readonly IRequestContextAccessor _accessor;

    /// <summary>Initialises a new <see cref="TenantContextHubFilter"/>.</summary>
    /// <param name="accessor">Reads the ambient request context.</param>
    public TenantContextHubFilter(IRequestContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Captures the connecting caller's request context and runs the rest of the connect pipeline inside it.
    /// </summary>
    /// <param name="context">The hub lifetime context for the connecting client.</param>
    /// <param name="next">The next delegate in the connect pipeline.</param>
    /// <returns>A task that completes when the connect pipeline has finished.</returns>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var requestContext = _accessor.Current
            ?? context.Context.GetHttpContext()?.RequestServices.GetService<IRequestContext>();

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

        if (ConnectionContext(invocationContext.Context) is not { } requestContext)
            return await next(invocationContext).ConfigureAwait(false);

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

        if (ConnectionContext(context.Context) is not { } requestContext)
        {
            await next(context, exception).ConfigureAwait(false);
            return;
        }

        using (RequestContextScope.Begin(requestContext))
        {
            await next(context, exception).ConfigureAwait(false);
        }
    }

    private static IRequestContext? ConnectionContext(HubCallerContext context) =>
        context.Items.TryGetValue(ConnectionContextKey, out var value) ? value as IRequestContext : null;
}
