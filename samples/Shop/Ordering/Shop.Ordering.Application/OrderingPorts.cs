using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using Shop.Ordering.Domain;

namespace Shop.Ordering.Application;

/// <summary>The permissions of Ordering's use cases.</summary>
public static class OrderingPermissions
{
    public const string Place = "orders.place";
    public const string Read = "orders.read";
    public const string Cancel = "orders.cancel";
}

/// <summary>Stock reservations in the Inventory service (gRPC over mutual TLS in production).</summary>
public interface IInventoryReservations
{
    /// <summary>Holds stock for every line, all or nothing; idempotent per order.</summary>
    Task<Result<Guid>> ReserveAsync(
        OrderId orderId,
        IReadOnlyList<(string Sku, int Quantity)> lines,
        CancellationToken ct
    );

    /// <summary>Releases what the order holds; idempotent.</summary>
    Task<Result<int>> ReleaseAsync(OrderId orderId, CancellationToken ct);
}

/// <summary>An order's new status, for whoever is watching (SignalR clients in production).</summary>
public sealed record OrderStatusNotice(Guid OrderId, Guid TenantId, string Status);

/// <summary>Tells connected clients an order changed status.</summary>
public interface IOrderStatusNotifier
{
    Task NotifyAsync(OrderStatusNotice notice, CancellationToken ct);
}

/// <summary>A tenant is required: Ordering fails closed without one.</summary>
public static class CallerTenant
{
    public static Result<TenantId> Of(SharedKernel.Execution.Context.IRequestContext caller) =>
        caller.TenantId is { } tenant
            ? Result<TenantId>.Success(tenant)
            : Result<TenantId>.Failure(
                SharedKernel.Primitives.Errors.Error.Forbidden(
                    "ordering.tenant_required",
                    "Ordering needs a caller with a tenant."
                )
            );
}
