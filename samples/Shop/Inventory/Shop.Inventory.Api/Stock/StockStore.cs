using System.Text.Json.Serialization;
using Dapper;
using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Dapper.Sessions;

namespace Shop.Inventory.Api.Stock;

/// <summary>A stock level: what is on hand, what is held for orders, what can still be sold.</summary>
public sealed record StockLevel(string Sku, int OnHand, int Reserved)
{
    public int Available => OnHand - Reserved;
}

[JsonSerializable(typeof(StockLevel))]
internal sealed partial class InventoryJsonContext : JsonSerializerContext;

/// <summary>
/// Stock levels per tenant in one Redis hash (<c>inventory:stock:{tenant}</c>, one field per SKU): the read path of
/// availability checks. PostgreSQL stays the source of truth; every write refreshes the field, and the reconciliation
/// job rebuilds the hashes from the database.
/// </summary>
public sealed class StockLevelCache(ITypedHashStore<StockLevel> hashes)
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromHours(1);

    public async ValueTask<StockLevel?> GetAsync(TenantId tenant, string sku, CancellationToken ct)
    {
        var lookup = await hashes.GetFieldAsync(Key(tenant), sku, ct);
        return lookup.TryGetValue(out StockLevel? level) ? level : null;
    }

    public ValueTask SetAsync(TenantId tenant, StockLevel level, CancellationToken ct) =>
        hashes.SetFieldAsync(Key(tenant), level.Sku, level, TimeToLive, ct);

    public ValueTask SetAllAsync(
        TenantId tenant,
        IReadOnlyDictionary<string, StockLevel> levels,
        CancellationToken ct
    ) => hashes.SetFieldsAsync(Key(tenant), levels, TimeToLive, ct);

    private static string Key(TenantId tenant) => $"inventory:stock:{tenant.Value:D}";
}

/// <summary>The SQL of the inventory tables. Every statement runs under row-level security: the session bound the tenant.</summary>
public static class StockSql
{
    public static Task<StockLevel?> GetAsync(
        IDbSession session,
        string sku,
        CancellationToken ct
    ) =>
        session.Connection.QuerySingleOrDefaultAsync<StockLevel>(
            session.Command(
                "SELECT sku AS Sku, on_hand AS OnHand, reserved AS Reserved FROM stock_items WHERE sku = @sku",
                new { sku },
                ct
            )
        );

    public static Task<int> UpsertOnHandAsync(
        IDbSession session,
        string sku,
        int onHand,
        CancellationToken ct
    ) =>
        session.Connection.ExecuteAsync(
            session.Command(
                """
                INSERT INTO stock_items (tenant_id, sku, on_hand) VALUES (@tenant, @sku, @onHand)
                ON CONFLICT (tenant_id, sku) DO UPDATE SET on_hand = EXCLUDED.on_hand
                """,
                new
                {
                    tenant = session.RequireTenantId().Value,
                    sku,
                    onHand,
                },
                ct
            )
        );

    public static Task<int> AddReservedAsync(
        IDbSession session,
        string sku,
        int delta,
        CancellationToken ct
    ) =>
        session.Connection.ExecuteAsync(
            session.Command(
                "UPDATE stock_items SET reserved = reserved + @delta WHERE sku = @sku",
                new { sku, delta },
                ct
            )
        );
}
