using Dapper;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace Shop.Inventory.Api.Stock;

/// <summary>The permissions of the inventory's use cases.</summary>
public static class InventoryPermissions
{
    /// <summary>Read stock levels (merchants).</summary>
    public const string Read = "inventory.read";

    /// <summary>Set stock on hand (merchants).</summary>
    public const string Manage = "inventory.manage";

    /// <summary>Reserve and release stock for orders (the Ordering service, by certificate).</summary>
    public const string Reserve = "inventory.reserve";
}

/// <summary>Errors of the inventory.</summary>
public static class InventoryErrors
{
    public static Error TenantRequired() =>
        Error.Forbidden(
            "inventory.tenant_required",
            "Inventory is only available to a caller with a tenant."
        );

    public static Error UnknownSku(string sku) =>
        Error.NotFound("inventory.unknown_sku", $"No stock is kept for SKU {sku}.");

    public static Error InsufficientStock(string sku, int requested, int available) =>
        Error.BusinessRule(
            "inventory.insufficient_stock",
            $"SKU {sku}: {requested} requested, {available} available."
        );

    public static Error Busy(string sku) =>
        Error.Conflict(
            "inventory.busy",
            $"SKU {sku} is being changed by another request; try again."
        );

    public static Error BelowReserved(string sku, int reserved) =>
        Error.BusinessRule(
            "inventory.below_reserved",
            $"SKU {sku} has {reserved} reserved; on hand cannot go below that."
        );
}

/// <summary>Sets what is on hand for a SKU, creating it if needed.</summary>
[RequirePermission(InventoryPermissions.Manage)]
public sealed record SetStockCommand(string Sku, int OnHand) : ICommand;

public sealed class SetStockHandler(
    IRequestContext caller,
    IDbSessionFactory sessions,
    SkuLocks locks,
    StockLevelCache cache
) : ICommandHandler<SetStockCommand>
{
    public async Task<Result> Handle(SetStockCommand command, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result.Failure(InventoryErrors.TenantRequired());
        }

        await using var held = await locks.AcquireAsync(tenant, [command.Sku], ct);
        if (held is null)
        {
            return Result.Failure(InventoryErrors.Busy(command.Sku));
        }

        await using var session = await sessions.OpenAsync(ct);
        var current = await StockSql.GetAsync(session, command.Sku, ct);
        if (current is not null && command.OnHand < current.Reserved)
        {
            return Result.Failure(InventoryErrors.BelowReserved(command.Sku, current.Reserved));
        }

        await StockSql.UpsertOnHandAsync(session, command.Sku, command.OnHand, ct);
        await session.CommitAsync(ct);
        await cache.SetAsync(
            tenant,
            new StockLevel(command.Sku, command.OnHand, current?.Reserved ?? 0),
            ct
        );
        return Result.Success();
    }
}

/// <summary>Reads a stock level: the Redis hash first, the database on a miss.</summary>
[RequirePermission(InventoryPermissions.Read)]
public sealed record GetStockQuery(string Sku) : IQuery<StockLevel>;

public sealed class GetStockHandler(
    IRequestContext caller,
    IDbSessionFactory sessions,
    StockLevelCache cache
) : IQueryHandler<GetStockQuery, StockLevel>
{
    public async Task<Result<StockLevel>> Handle(GetStockQuery query, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result<StockLevel>.Failure(InventoryErrors.TenantRequired());
        }

        if (await cache.GetAsync(tenant, query.Sku, ct) is { } cached)
        {
            return Result<StockLevel>.Success(cached);
        }

        await using var session = await sessions.OpenReadOnlyAsync(ct);
        var level = await StockSql.GetAsync(session, query.Sku, ct);
        if (level is null)
        {
            return Result<StockLevel>.Failure(InventoryErrors.UnknownSku(query.Sku));
        }

        await cache.SetAsync(tenant, level, ct);
        return Result<StockLevel>.Success(level);
    }
}

/// <summary>One line of a reservation.</summary>
public sealed record ReservationLine(string Sku, int Quantity);

/// <summary>Holds stock for every line of an order, all or nothing. Idempotent per order.</summary>
[RequirePermission(InventoryPermissions.Reserve)]
public sealed record ReserveStockCommand(Guid OrderId, IReadOnlyList<ReservationLine> Lines)
    : ICommand<Guid>;

public sealed class ReserveStockHandler(
    IRequestContext caller,
    IDbSessionFactory sessions,
    SkuLocks locks,
    StockLevelCache cache
) : ICommandHandler<ReserveStockCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ReserveStockCommand command, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result<Guid>.Failure(InventoryErrors.TenantRequired());
        }

        // Read, check, write: safe only because every SKU of the order is locked across replicas first.
        await using var held = await locks.AcquireAsync(
            tenant,
            command.Lines.Select(l => l.Sku),
            ct
        );
        if (held is null)
        {
            return Result<Guid>.Failure(
                InventoryErrors.Busy(string.Join(", ", command.Lines.Select(l => l.Sku)))
            );
        }

        await using var session = await sessions.OpenAsync(ct);
        var existing = await session.Connection.QueryFirstOrDefaultAsync<Guid?>(
            session.Command(
                "SELECT id FROM reservations WHERE order_id = @orderId AND NOT released ORDER BY id LIMIT 1",
                new { orderId = command.OrderId },
                ct
            )
        );
        if (existing is { } already)
        {
            return Result<Guid>.Success(already);
        }

        var levels = new List<StockLevel>();
        foreach (var line in command.Lines)
        {
            var level = await StockSql.GetAsync(session, line.Sku, ct);
            if (level is null)
            {
                return Result<Guid>.Failure(InventoryErrors.UnknownSku(line.Sku));
            }

            if (level.Available < line.Quantity)
            {
                return Result<Guid>.Failure(
                    InventoryErrors.InsufficientStock(line.Sku, line.Quantity, level.Available)
                );
            }

            await StockSql.AddReservedAsync(session, line.Sku, line.Quantity, ct);
            levels.Add(level with { Reserved = level.Reserved + line.Quantity });
        }

        Guid reservationId = Guid.CreateVersion7();
        foreach (var line in command.Lines)
        {
            await session.Connection.ExecuteAsync(
                session.Command(
                    """
                    INSERT INTO reservations (id, tenant_id, order_id, sku, quantity)
                    VALUES (@id, @tenant, @orderId, @sku, @quantity)
                    """,
                    new
                    {
                        id = line == command.Lines[0] ? reservationId : Guid.CreateVersion7(),
                        tenant = tenant.Value,
                        orderId = command.OrderId,
                        sku = line.Sku,
                        quantity = line.Quantity,
                    },
                    ct
                )
            );
        }

        await session.CommitAsync(ct);
        foreach (var level in levels)
        {
            await cache.SetAsync(tenant, level, ct);
        }

        return Result<Guid>.Success(reservationId);
    }
}

/// <summary>Releases what an order holds (the compensation of <see cref="ReserveStockCommand"/>). Idempotent.</summary>
[RequirePermission(InventoryPermissions.Reserve)]
public sealed record ReleaseStockCommand(Guid OrderId) : ICommand<int>;

public sealed class ReleaseStockHandler(
    IRequestContext caller,
    IDbSessionFactory sessions,
    SkuLocks locks,
    StockLevelCache cache
) : ICommandHandler<ReleaseStockCommand, int>
{
    public async Task<Result<int>> Handle(ReleaseStockCommand command, CancellationToken ct)
    {
        if (caller.TenantId is not { } tenant)
        {
            return Result<int>.Failure(InventoryErrors.TenantRequired());
        }

        IReadOnlyList<ReservationLine> held;
        await using (var lookup = await sessions.OpenReadOnlyAsync(ct))
        {
            held = (
                await lookup.Connection.QueryAsync<ReservationLine>(
                    lookup.Command(
                        "SELECT sku AS Sku, quantity AS Quantity FROM reservations WHERE order_id = @orderId AND NOT released",
                        new { orderId = command.OrderId },
                        ct
                    )
                )
            ).ToList();
        }

        if (held.Count == 0)
        {
            return Result<int>.Success(0);
        }

        await using var locked = await locks.AcquireAsync(tenant, held.Select(l => l.Sku), ct);
        if (locked is null)
        {
            return Result<int>.Failure(
                InventoryErrors.Busy(string.Join(", ", held.Select(l => l.Sku)))
            );
        }

        await using var session = await sessions.OpenAsync(ct);
        int released = await session.Connection.ExecuteAsync(
            session.Command(
                "UPDATE reservations SET released = true WHERE order_id = @orderId AND NOT released",
                new { orderId = command.OrderId },
                ct
            )
        );
        var levels = new List<StockLevel>();
        foreach (var line in held)
        {
            await StockSql.AddReservedAsync(session, line.Sku, -line.Quantity, ct);
            if (await StockSql.GetAsync(session, line.Sku, ct) is { } level)
            {
                levels.Add(level);
            }
        }

        await session.CommitAsync(ct);
        foreach (var level in levels)
        {
            await cache.SetAsync(tenant, level, ct);
        }

        return Result<int>.Success(released);
    }
}

/// <summary>
/// One Redis lock per SKU, taken in a fixed order so two orders sharing SKUs cannot deadlock. A request that cannot
/// get every lock within the wait time gets nothing and fails as busy.
/// </summary>
public sealed class SkuLocks(IDistributedLockService locks)
{
    private static readonly DistributedLockOptions Options = new()
    {
        Expiry = TimeSpan.FromSeconds(15),
        WaitTime = TimeSpan.FromSeconds(10),
    };

    public async ValueTask<IAsyncDisposable?> AcquireAsync(
        TenantId tenant,
        IEnumerable<string> skus,
        CancellationToken ct
    )
    {
        var held = new List<IDistributedLock>();
        foreach (string sku in skus.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var handle = await locks.TryAcquireAsync(
                $"inventory:{tenant.Value:N}:{sku}",
                Options,
                ct
            );
            if (handle is null)
            {
                await ReleaseAll(held);
                return null;
            }

            held.Add(handle);
        }

        return new Held(held);
    }

    private static async ValueTask ReleaseAll(List<IDistributedLock> held)
    {
        foreach (var handle in held)
        {
            await handle.DisposeAsync();
        }
    }

    private sealed class Held(List<IDistributedLock> held) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ReleaseAll(held);
    }
}
