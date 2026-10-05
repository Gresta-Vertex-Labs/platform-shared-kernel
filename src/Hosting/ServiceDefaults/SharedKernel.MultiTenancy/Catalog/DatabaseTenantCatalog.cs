using SharedKernel.Execution.Tenancy;
using System.Data;
using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// <see cref="ITenantCatalog"/> implementation backed by a consumer-owned tenant directory table,
/// queried via <see cref="IDbConnectionFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Reuses the exact <see cref="IDbConnectionFactory"/>/parameterized-query pattern
/// <see cref="Resolution.DatabaseTenantResolutionStrategy"/> already established — not a second,
/// independently-invented data-access path. Uses a parameterized query exclusively: the
/// request-derived <c>resolutionKey</c> value is never string-interpolated or concatenated into
/// SQL text.
/// </para>
/// <para>
/// Expects a tenant directory table shaped as <c>tenant_directory(tenant_id uuid,
/// resolution_key text, display_name text, status text, isolation_mode text,
/// default_culture text)</c>. Adjust <see cref="ByIdCommandText"/>/
/// <see cref="ByResolutionKeyCommandText"/> to match your own schema if it differs — this is a
/// composition-root recipe, not a fixed migration.
/// </para>
/// <para>
/// <b>This default recipe always returns an empty <see cref="TenantDescriptor.Settings"/>
/// dictionary</b> — it does not assume a particular settings-storage shape (a separate key/value
/// table, a JSONB column, …). Subclass or wrap this type to populate <c>Settings</c> from your
/// own schema if you need it.
/// </para>
/// </remarks>
public sealed class DatabaseTenantCatalog(IDbConnectionFactory connectionFactory) : ITenantCatalog
{
    private const string ByIdCommandText =
        "SELECT tenant_id, display_name, status, isolation_mode, default_culture "
        + "FROM tenant_directory WHERE tenant_id = @tenantId";

    private const string ByResolutionKeyCommandText =
        "SELECT tenant_id, display_name, status, isolation_mode, default_culture "
        + "FROM tenant_directory WHERE resolution_key = @resolutionKey";

    /// <inheritdoc/>
    public Task<TenantDescriptor?> GetByIdAsync(TenantId tenantId, CancellationToken ct) =>
        QueryAsync(ByIdCommandText, "@tenantId", tenantId.Value, ct);

    /// <inheritdoc/>
    public Task<TenantDescriptor?> GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolutionKey);

        return QueryAsync(ByResolutionKeyCommandText, "@resolutionKey", resolutionKey, ct);
    }

    private async Task<TenantDescriptor?> QueryAsync(
        string commandText,
        string parameterName,
        object parameterValue,
        CancellationToken ct)
    {
        var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = commandText;

            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.Value = parameterValue;
            command.Parameters.Add(parameter);

            using var reader = command is DbCommand dbCommand
                ? await dbCommand.ExecuteReaderAsync(ct).ConfigureAwait(false)
                : command.ExecuteReader();

            var hasRow = reader is DbDataReader dbReader
                ? await dbReader.ReadAsync(ct).ConfigureAwait(false)
                : reader.Read();

            if (!hasRow)
            {
                return null;
            }

            var tenantId = new TenantId(reader.GetGuid(0));
            var displayName = reader.GetString(1);
            var status = Enum.Parse<TenantStatus>(reader.GetString(2), ignoreCase: true);
            var isolationMode = Enum.Parse<TenantIsolationMode>(reader.GetString(3), ignoreCase: true);
            var defaultCulture = reader.IsDBNull(4) ? null : reader.GetString(4);

            return new TenantDescriptor(
                tenantId,
                displayName,
                status,
                isolationMode,
                defaultCulture,
                EmptySettings);
        }
        finally
        {
            connection.Dispose();
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();
}
