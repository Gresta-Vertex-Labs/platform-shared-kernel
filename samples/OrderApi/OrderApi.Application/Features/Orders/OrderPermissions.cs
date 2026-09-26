namespace OrderApi.Application.Features.Orders;

/// <summary>The permissions the order use cases declare with <c>[RequirePermission]</c>.</summary>
public static class OrderPermissions
{
    /// <summary>Cancel an order.</summary>
    public const string Cancel = "orders.cancel";
}
