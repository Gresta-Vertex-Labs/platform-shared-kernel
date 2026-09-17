using Microsoft.AspNetCore.SignalR;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>
/// Attaches the resolved tenant identifier to the connection's <see cref="HubCallerContext.Items"/>
/// at connect time.
/// </summary>
/// <remarks>
/// Resolves <see cref="ITenantProvider"/> (<c>12.Security.Abstractions</c>) from the connection's
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> in <see cref="OnConnectedAsync"/>; stores
/// <see cref="ITenantProvider.TenantId"/> in <c>Context.Items["TenantId"]</c> for the lifetime of
/// the connection so hub methods read it without re-resolving per invocation. Does not reject
/// connections with no tenant — that policy decision belongs to the consuming service's Hub (via
/// <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute"/> or explicit checks); this
/// filter only attaches data.
/// </remarks>
public sealed class TenantContextHubFilter : IHubFilter
{
    /// <summary>The <see cref="HubCallerContext.Items"/> key the resolved tenant identifier is stored under.</summary>
    public const string ItemsKey = "TenantId";

    /// <summary>
    /// Resolves the current tenant from the connection's <see cref="Microsoft.AspNetCore.Http.HttpContext"/> and stores it
    /// in <see cref="HubCallerContext.Items"/> before invoking the rest of the connect pipeline.
    /// </summary>
    /// <param name="context">The hub lifetime context for the connecting client.</param>
    /// <param name="next">The next delegate in the connect pipeline.</param>
    /// <returns>A task that completes when the connect pipeline has finished.</returns>
    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var httpContext = context.Context.GetHttpContext();
        var tenantProvider = httpContext?.RequestServices.GetService(typeof(ITenantProvider)) as ITenantProvider;

        context.Context.Items[ItemsKey] = tenantProvider?.TenantId ?? Guid.Empty;

        return next(context);
    }
}
