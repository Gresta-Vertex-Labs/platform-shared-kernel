using Microsoft.AspNetCore.SignalR;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.SignalR.Filters;

namespace SharedKernel.Presentation.SignalR;

/// <summary>Reads the tenant and the correlation id of the connection a hub method runs for.</summary>
/// <remarks>
/// Both come from the connection's <see cref="IRequestContext"/>: the context of the request that opened the
/// connection, which <c>AddSharedKernelSignalR()</c>'s hub filter captures at connect time and reopens as the ambient
/// <see cref="RequestContextScope"/> around every hub method (P-579). Inside a hub method, an injected
/// <see cref="IRequestContext"/> or <see cref="IRequestContextAccessor"/> reads the same context.
/// </remarks>
public static class HubCallerContextExtensions
{
    /// <summary>Returns the tenant of the connection.</summary>
    /// <param name="context">The hub's <c>Context</c>.</param>
    /// <returns>
    /// The tenant of the connection's request context — the one the caller's credential asserts, or the one
    /// <c>SharedKernel.MultiTenancy</c>'s tenant resolution resolved — or <see langword="null"/> when it has none.
    /// </returns>
    /// <remarks>
    /// A connection without a tenant gets <see langword="null"/>, so it cannot be put into a tenant group shared with
    /// every other tenantless connection; see <see cref="HubGroupNaming.TenantGroup(TenantId)"/>. Requires
    /// <c>SharedKernel.ServiceDefaults.Security</c>'s <c>AddSharedKernelRequestContext()</c> and
    /// <c>UseSharedKernelRequestContext()</c>.
    /// </remarks>
    public static TenantId? GetTenantId(this HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return ConnectionContext(context)?.TenantId;
    }

    /// <summary>Returns the correlation id of the connection.</summary>
    /// <param name="context">The hub's <c>Context</c>.</param>
    /// <returns>
    /// The correlation id <c>UseSharedKernelRequestContext()</c> resolved for the request that opened the connection —
    /// the validated inbound <c>X-Correlation-Id</c>, or a new id when the client sent none or an invalid one — or
    /// <see langword="null"/> when that middleware did not run.
    /// </returns>
    public static string? GetCorrelationId(this HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return ConnectionContext(context)?.CorrelationId;
    }

    // The context the hub filter captured; without the filter (a hub registered without AddSharedKernelSignalR), the
    // ambient one, which the connect handler of a WebSockets connection still runs in.
    private static IRequestContext? ConnectionContext(HubCallerContext context) =>
        RequestContextHubFilter.GetConnectionContext(context) ?? RequestContextScope.Current;
}
