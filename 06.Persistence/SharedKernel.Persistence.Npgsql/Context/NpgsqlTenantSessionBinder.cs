using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;

namespace SharedKernel.Persistence.Npgsql.Context;

/// <summary>
/// PostgreSQL implementation of <see cref="ITenantSessionBinder"/>: one
/// <c>SELECT set_config('app.tenant_id', $1, true)</c> on the given transaction.
/// </summary>
/// <remarks>
/// The setting is transaction-local, so it is gone when the transaction commits or rolls back — the
/// physical connection returns to the pool (or to PgBouncer) with nothing bound. A <see langword="null"/>
/// tenant binds the empty string, which the policy predicate of <see cref="TenantSessionSql"/> treats as
/// "no rows".
/// </remarks>
internal sealed class NpgsqlTenantSessionBinder : ITenantSessionBinder
{
    // Positional parameter: an explicitly named "$1" parameter does not bind to the positional
    // placeholder in Npgsql and fails with 08P01.
    private const string BindSql = "SELECT set_config('" + TenantSessionSql.TenantIdSetting + "', $1, true)";

    /// <inheritdoc />
    public async Task BindAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BindSql;

        var value = command.CreateParameter();
        value.Value = tenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
