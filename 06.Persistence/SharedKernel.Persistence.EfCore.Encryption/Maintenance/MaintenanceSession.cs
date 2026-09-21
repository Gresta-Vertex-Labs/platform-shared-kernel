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
/// Connects, in order of preference, through the data source given to <c>UseMaintenanceDataSource</c>, the
/// cross-tenant data source <c>AddSharedKernelNpgsql</c> registers for row-level security
/// (<see cref="NpgsqlDataSourceKeys.CrossTenant"/>, a role that bypasses it), or the context's own connection, opened through
/// EF Core so connection interceptors run. The platform's cross-tenant scope is entered for the whole operation.
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
    private readonly IDisposable? _crossTenantScope;

    private MaintenanceSession(
        DbContext context, DbConnection connection, bool ownsConnection, bool closeContextConnection, bool requireBypass, IDisposable? crossTenantScope)
    {
        _context = context;
        Connection = connection;
        _ownsConnection = ownsConnection;
        _closeContextConnection = closeContextConnection;
        _requireBypass = requireBypass;
        _crossTenantScope = crossTenantScope;
    }

    public DbConnection Connection { get; }

    public static async Task<MaintenanceSession> OpenAsync(
        DbContext context,
        FieldEncryptionRuntime runtime,
        EncryptionOptions options,
        IServiceProvider services,
        string reason,
        CancellationToken cancellationToken)
    {
        // The context's own bypass (the resolving scope's, or the explicit caller's): stays active for the whole
        // operation because the state lives on the scope instance, not in this method's async flow.
        var scope = (context as SharedKernelDbContext)?.CrossTenantScope.Enter(reason)
            ?? services.GetService<ICrossTenantScope>()?.Enter(reason);
        try
        {
            var dataSource = runtime.Settings.MaintenanceDataSourceFactory?.Invoke(services)
                ?? (services as IKeyedServiceProvider)?.GetKeyedService(typeof(NpgsqlDataSource), NpgsqlDataSourceKeys.CrossTenant) as DbDataSource;
            if (dataSource is not null)
            {
                var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                return new MaintenanceSession(context, connection, ownsConnection: true, closeContextConnection: false, options.RequireRowSecurityBypass, scope);
            }

            var wasOpen = context.Database.GetDbConnection().State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            return new MaintenanceSession(
                context, context.Database.GetDbConnection(), ownsConnection: false, closeContextConnection: !wasOpen, options.RequireRowSecurityBypass, scope);
        }
        catch
        {
            scope?.Dispose();
            throw;
        }
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
                "FORCE ROW LEVEL SECURITY, or a BYPASSRLS role): configure the row-level security cross-tenant connection " +
                "string of AddSharedKernelNpgsql, or 'UseFieldEncryption(k => k.UseMaintenanceDataSource(...))'. A role that " +
                "sees every row through a policy of its own needs EncryptionOptions.RequireRowSecurityBypass = false. " +
                "Processing only the visible rows would " +
                "report completion while other tenants' rows still use the old key.",
                exception)
            : null;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ownsConnection)
                await Connection.DisposeAsync().ConfigureAwait(false);
            else if (_closeContextConnection)
                await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            _crossTenantScope?.Dispose();
        }
    }
}
