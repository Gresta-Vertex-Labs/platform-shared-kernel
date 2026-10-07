using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Connections;

namespace Shop.Ordering.Infrastructure.Persistence;

/// <summary>An order's columns exactly as stored (ciphertext) and the audit actions recorded for it.</summary>
public sealed record StoredOrder(
    string CustomerEmailColumn,
    string ShippingAddressColumn,
    IReadOnlyList<string> AuditActions
);

/// <summary>
/// Reads the raw rows behind an order over the cross-tenant data source, bypassing EF Core's decryption, so the
/// end-to-end tests can prove what actually reaches PostgreSQL.
/// </summary>
public sealed class OrderStorageInspector(
    [FromKeyedServices(NpgsqlDataSourceKeys.CrossTenant)] NpgsqlDataSource crossTenant
)
{
    public async Task<StoredOrder?> ReadAsync(
        Guid orderId,
        string? idempotencyKey,
        CancellationToken ct
    )
    {
        await using var connection = await crossTenant.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<(
            string CustomerEmail,
            string ShippingAddress
        )>(
            new CommandDefinition(
                "SELECT customer_email, shipping_address FROM orders WHERE id = @id",
                new { id = orderId },
                cancellationToken: ct
            )
        );
        if (row == default)
        {
            return null;
        }

        var actions = await connection.QueryAsync<string>(
            new CommandDefinition(
                "SELECT action FROM audit_records WHERE resource_id IN (@id, @key) ORDER BY occurred_on",
                new { id = orderId.ToString("D"), key = idempotencyKey ?? string.Empty },
                cancellationToken: ct
            )
        );
        return new StoredOrder(row.CustomerEmail, row.ShippingAddress, [.. actions]);
    }
}
