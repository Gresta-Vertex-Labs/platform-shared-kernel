using Microsoft.AspNetCore.SignalR;
using SharedKernel.Execution.Context;
using SharedKernel.Presentation.Authorization;
using Shop.Ordering.Application;

namespace Shop.Ordering.Api;

/// <summary>
/// Live order status for a tenant's clients. A connection joins its tenant's group when it opens; nothing is sent
/// across tenants.
/// </summary>
[RequireEndpointPermission(OrderingPermissions.Read)]
public sealed class OrdersHub(IRequestContext caller) : Hub
{
    public const string Path = "/hubs/orders";
    public const string StatusChanged = "orderStatusChanged";

    public override async Task OnConnectedAsync()
    {
        if (caller.TenantId is { } tenant)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenant.Value));
        }

        await base.OnConnectedAsync();
    }

    internal static string TenantGroup(Guid tenant) => $"tenant:{tenant:D}";
}

/// <summary>Pushes status changes to the order's tenant group.</summary>
public sealed class SignalROrderStatusNotifier(IHubContext<OrdersHub> hub) : IOrderStatusNotifier
{
    public Task NotifyAsync(OrderStatusNotice notice, CancellationToken ct) =>
        hub
            .Clients.Group(OrdersHub.TenantGroup(notice.TenantId))
            .SendAsync(OrdersHub.StatusChanged, notice, ct);
}
