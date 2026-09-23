using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.SignalR;

/// <summary>Reads the tenant and the correlation id of the connection a hub method runs for.</summary>
public static class HubCallerContextExtensions
{
    /// <summary>Returns the tenant of the connection.</summary>
    /// <param name="context">The hub's <c>Context</c>.</param>
    /// <returns>
    /// The tenant from the registered <see cref="ITenantProvider"/>, or <see langword="null"/> when no provider is
    /// registered, the connection has no HTTP context, or the provider reports no tenant (<see cref="Guid.Empty"/>).
    /// </returns>
    /// <remarks>
    /// The provider is resolved from the connection's request services, so it sees the caller that opened the
    /// connection. A connection without a tenant gets <see langword="null"/>, never <see cref="Guid.Empty"/>, so it
    /// cannot be put into a tenant group shared with every other tenantless connection; see
    /// <see cref="HubGroupNaming.TenantGroup"/>.
    /// </remarks>
    public static Guid? GetTenantId(this HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tenantProvider = context.GetHttpContext()?.RequestServices?.GetService<ITenantProvider>();
        var tenantId = tenantProvider?.TenantId ?? Guid.Empty;

        return tenantId == Guid.Empty ? null : tenantId;
    }

    /// <summary>Returns the correlation id of the connection.</summary>
    /// <param name="context">The hub's <c>Context</c>.</param>
    /// <returns>
    /// The correlation id <c>UseSharedKernelWebApi()</c> resolved for the request that opened the connection — the
    /// validated inbound <c>X-Correlation-Id</c>, or the id assigned when the client sent none — or
    /// <see langword="null"/> when that middleware did not run or the connection has no HTTP context.
    /// </returns>
    public static string? GetCorrelationId(this HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetHttpContext()?.GetCorrelationId();
    }
}
