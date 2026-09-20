using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Npgsql.Context;

/// <summary>
/// PostgreSQL implementation of <see cref="ITenantSessionBinder"/> using
/// <c>set_config('app.tenant_id'/'app.cross_tenant',..., is_local)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BindAsync"/> passes <c>is_local =&gt; true</c> so both settings are transaction-scoped:
/// PostgreSQL automatically discards them when the transaction commits or rolls back, so a pooled
/// connection can never carry a stale binding into its next lease through this path alone.
/// <see cref="BindConnectionAsync"/> passes <c>is_local =&gt; false</c> (session-scoped) instead, since
/// its caller cannot guarantee an explicit transaction wraps every subsequent command —
/// <see cref="ResetConnectionAsync"/> is the caller's responsibility to invoke before the connection
/// returns to the pool.
/// </para>
/// <para>
/// A <see langword="null"/> <c>tenantId</c> binds the empty string, matching the RLS policy's own
/// <c>NULLIF(current_setting('app.tenant_id', true), '')</c> "no tenant bound" convention.
/// </para>
/// <para>
/// Pair this with a row-level security policy defined against <c>current_setting('app.tenant_id',
/// true)</c>/<c>current_setting('app.cross_tenant', true)</c> on every tenant-scoped table — see
/// <c>SharedKernel.Persistence.PostgreSQL</c>'s RLS migration helper. Binding the session settings
/// with no matching policy enforces nothing.
/// </para>
/// <para>
/// <strong>Cross-tenant escape token:</strong> the value written for an active
/// <see cref="ICrossTenantScope"/> is <paramref name="crossTenantEscapeToken"/> — see
/// <c>NpgsqlPersistenceOptions.CrossTenantEscapeToken</c>'s own remarks for why the literal
/// <c>"on"</c> default it falls back to is guessable, and how to harden it.
/// </para>
/// </remarks>
public sealed class NpgsqlTenantSessionBinder : ITenantSessionBinder
{
    private const string TenantSettingName = "app.tenant_id";
    private const string CrossTenantSettingName = "app.cross_tenant";
    private const string DefaultCrossTenantOn = "on";
    private const string CrossTenantOff = "off";

    private readonly string _crossTenantOn;

    /// <summary>Initialises a new <see cref="NpgsqlTenantSessionBinder"/>.</summary>
    /// <param name="crossTenantEscapeToken">
    /// The value to write for an active <see cref="ICrossTenantScope"/>, and that a matching
    /// row-level security policy's escape clause must compare against. Defaults to the literal
    /// <c>"on"</c> when omitted — see <c>NpgsqlPersistenceOptions.CrossTenantEscapeToken</c>'s remarks.
    /// </param>
    public NpgsqlTenantSessionBinder(string? crossTenantEscapeToken = null)
    {
        _crossTenantOn = string.IsNullOrEmpty(crossTenantEscapeToken) ? DefaultCrossTenantOn : crossTenantEscapeToken;
    }

    /// <inheritdoc />
    public async Task BindAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid? tenantId,
        bool crossTenantActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await SetConfigAsync(
            connection, transaction, TenantSettingName, tenantId?.ToString() ?? string.Empty,
            isLocal: true, cancellationToken);
        await SetConfigAsync(
            connection, transaction, CrossTenantSettingName, crossTenantActive ? _crossTenantOn : CrossTenantOff,
            isLocal: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task BindConnectionAsync(
        DbConnection connection,
        Guid? tenantId,
        bool crossTenantActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await SetConfigAsync(
            connection, transaction: null, TenantSettingName, tenantId?.ToString() ?? string.Empty,
            isLocal: false, cancellationToken);
        await SetConfigAsync(
            connection, transaction: null, CrossTenantSettingName, crossTenantActive ? _crossTenantOn : CrossTenantOff,
            isLocal: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ResetConnectionAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await SetConfigAsync(connection, transaction: null, TenantSettingName, string.Empty, isLocal: false, cancellationToken);
        await SetConfigAsync(connection, transaction: null, CrossTenantSettingName, CrossTenantOff, isLocal: false, cancellationToken);
    }

    // Shared set_config($1, $2, $3) issuer for every bind/reset member above — positional (unnamed)
    // parameters only, per the platform's established Npgsql pitfall: an explicitly-named "$1"
    // parameter does NOT bind to the positional "$1" placeholder and fails with 08P01.
    private static async Task SetConfigAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string settingName,
        string value,
        bool isLocal,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = isLocal
            ? "SELECT set_config($1, $2, true)"
            : "SELECT set_config($1, $2, false)";

        var settingNameParameter = command.CreateParameter();
        settingNameParameter.Value = settingName;
        command.Parameters.Add(settingNameParameter);

        var valueParameter = command.CreateParameter();
        valueParameter.Value = value;
        command.Parameters.Add(valueParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
