using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.Interception;
using SharedKernel.Persistence.Npgsql.Connections;

namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>
/// The connection a maintenance operation (key rotation, plaintext migration, tenant shredding) runs on, and its
/// short per-batch transactions.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Authorization.</strong> Every maintenance operation reaches every tenant's rows, so it requires a
/// cross-tenant scope that the caller already entered (<c>ICrossTenantScope.Enter(reason)</c>, in a scope whose
/// <c>IRequestContext</c> names the operator or job). It never enters one itself: the entry — who, and why — is the
/// caller's decision and is logged under the caller's identity.
/// </para>
/// <para>
/// Connects, in order of preference, through the data source given to <c>UseMaintenanceDataSource</c>, the
/// cross-tenant data source registered for row-level security (<see cref="NpgsqlDataSourceKeys.CrossTenant"/>, a role
/// that bypasses it), or the context's own connection, opened through EF Core so connection interceptors run.
/// </para>
/// <para>
/// <strong>Never a silent partial scan:</strong> with <see cref="EncryptionOptions.RequireRowSecurityBypass"/> each
/// transaction sets <c>row_security = off</c>. PostgreSQL then raises an error for any statement a row-level
/// security policy would filter, instead of returning only the rows the role may see; a maintenance run that
/// reported "complete" over a filtered table would lead an operator to retire a key still in use.
/// </para>
/// </remarks>
internal sealed class MaintenanceSession : IAsyncDisposable
{
    private readonly DbContext _context;
    private readonly bool _ownsConnection;
    private readonly bool _closeContextConnection;
    private readonly bool _requireBypass;

    private MaintenanceSession(
        DbContext context, DbConnection connection, bool ownsConnection, bool closeContextConnection, bool requireBypass)
    {
        _context = context;
        Connection = connection;
        _ownsConnection = ownsConnection;
        _closeContextConnection = closeContextConnection;
        _requireBypass = requireBypass;
    }

    public DbConnection Connection { get; }

    /// <summary>
    /// Throws unless a cross-tenant scope is active for <paramref name="context"/>: the context's own (the resolving
    /// dependency-injection scope's, or the explicit caller's of <c>ICallerDbContextFactory</c>), else the scope's
    /// registered <see cref="ICrossTenantScope"/>.
    /// </summary>
    /// <param name="context">The context the operation runs for.</param>
    /// <param name="services">The resolving services.</param>
    /// <param name="operation">The operation, for the message.</param>
    /// <exception cref="InvalidOperationException">No cross-tenant scope is active.</exception>
    public static void RequireCrossTenantScope(DbContext context, IServiceProvider services, string operation)
    {
        var active = context is SharedKernelDbContext platform
            ? platform.CrossTenantScope.IsActive
            : services.GetService<ICrossTenantScope>()?.IsActive == true;
        if (active)
            return;

        throw new InvalidOperationException(
            $"'{operation}' reaches every tenant's rows and requires an active cross-tenant scope, entered by the caller " +
            "around the call: 'using (crossTenantScope.Enter(\"<why>\")) { ... }' with the ICrossTenantScope of the same " +
            "dependency-injection scope (or 'context.CrossTenantScope' for a context from ICallerDbContextFactory). Run it in a " +
            "scope whose IRequestContext identifies the operator or job (for example a SystemRequestContext), so the entry is " +
            "attributable. Maintenance never enters the scope itself.");
    }

    public static async Task<MaintenanceSession> OpenAsync(
        DbContext context,
        FieldEncryptionRuntime runtime,
        EncryptionOptions options,
        IServiceProvider services,
        string operation,
        CancellationToken cancellationToken)
    {
        RequireCrossTenantScope(context, services, operation);

        var dataSource = runtime.Settings.MaintenanceDataSourceFactory?.Invoke(services)
            ?? (services as IKeyedServiceProvider)?.GetKeyedService(typeof(NpgsqlDataSource), NpgsqlDataSourceKeys.CrossTenant) as DbDataSource;
        if (dataSource is not null)
        {
            var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return new MaintenanceSession(context, connection, ownsConnection: true, closeContextConnection: false, options.RequireRowSecurityBypass);
        }

        var wasOpen = context.Database.GetDbConnection().State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        return new MaintenanceSession(
            context, context.Database.GetDbConnection(), ownsConnection: false, closeContextConnection: !wasOpen, options.RequireRowSecurityBypass);
    }

    /// <summary>Begins a transaction, with row security off when required.</summary>
    public async Task<DbTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await Connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (_requireBypass)
        {
            await using var command = Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SET LOCAL row_security = off";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return transaction;
    }

    /// <summary>Turns PostgreSQL's "would be affected by row-level security" error into an actionable one.</summary>
    public static Exception? TranslateRowSecurityError(Exception exception, string table) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege } postgres
        && postgres.MessageText.Contains("row-level security", StringComparison.OrdinalIgnoreCase)
            ? new InvalidOperationException(
                $"Encryption maintenance cannot see every row of {table}: a row-level security policy applies to the " +
                "connecting role. Run maintenance as a role that bypasses row-level security (the table owner without " +
                "FORCE ROW LEVEL SECURITY, or a BYPASSRLS role): configure the cross-tenant connection string of the data " +
                "source (RowLevelSecurity:CrossTenantConnectionString), or 'UseFieldEncryption(k => k.UseMaintenanceDataSource(...))'. " +
                "A role that sees every row through a policy of its own needs EncryptionOptions.RequireRowSecurityBypass = false. " +
                "Processing only the visible rows would report completion while other tenants' rows still use the old key.",
                exception)
            : null;

    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection)
            await Connection.DisposeAsync().ConfigureAwait(false);
        else if (_closeContextConnection)
            await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
    }
}
