using System.Data.Common;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Dapper.Diagnostics;
using SharedKernel.Persistence.Dapper.Options;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Options;

namespace SharedKernel.Persistence.Dapper.Sessions;

/// <summary>The scoped <see cref="IDbSessionFactory"/>; see the interface for the rules.</summary>
internal sealed class DbSessionFactory : IDbSessionFactory
{
    private const string ReadOnlySql = "SET TRANSACTION READ ONLY";

    private static readonly DbSessionOptions ReadWrite = new();
    private static readonly DbSessionOptions ReadOnly = new() { ReadOnly = true };

    private readonly IServiceProvider _services;
    private readonly IRequestContext _requestContext;
    private readonly ICrossTenantScope _crossTenantScope;
    private readonly IAmbientDbTransaction? _ambientTransaction;
    private readonly int? _commandTimeoutSeconds;
    private readonly bool _rowLevelSecurity;
    private readonly ILogger _logger;

    public DbSessionFactory(
        IServiceProvider services,
        IRequestContext requestContext,
        ICrossTenantScope crossTenantScope,
        IOptions<DapperPersistenceOptions> options,
        IAmbientDbTransaction? ambientTransaction = null,
        IOptionsMonitor<NpgsqlPersistenceOptions>? npgsqlOptions = null,
        ILogger<DbSessionFactory>? logger = null)
    {
        _services = services;
        _requestContext = requestContext;
        _crossTenantScope = crossTenantScope;
        _ambientTransaction = ambientTransaction;
        _commandTimeoutSeconds = options.Value.DefaultCommandTimeoutSeconds;
        _rowLevelSecurity = npgsqlOptions?.Get(Microsoft.Extensions.Options.Options.DefaultName).RowLevelSecurity.Enabled == true;
        _logger = logger ?? NullLogger<DbSessionFactory>.Instance;
    }

    public Task<IDbSession> OpenAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(ReadWrite, cancellationToken);

    public Task<IDbSession> OpenReadOnlyAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(ReadOnly, cancellationToken);

    public async Task<IDbSession> OpenAsync(DbSessionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tenantId = _requestContext.TenantId;
        var crossTenant = _rowLevelSecurity && _crossTenantScope.IsActive;

        if (options.EnlistInAmbientTransaction && _ambientTransaction?.Current is { } ambient)
        {
            if (crossTenant)
            {
                throw new InvalidOperationException(
                    "A cross-tenant scope is active inside a unit of work whose transaction runs on the application "
                        + "database role, which row-level security limits to one tenant. Open the session with "
                        + "EnlistInAmbientTransaction = false, or enter the scope outside the unit of work.");
            }

            if (_rowLevelSecurity)
                await BindTenantAsync(ambient.Connection, ambient.Transaction, tenantId, cancellationToken).ConfigureAwait(false);

            return new DbSession(ambient.Connection, ambient.Transaction, owned: false, readOnly: false, tenantId, _commandTimeoutSeconds, _logger);
        }

        var connectionFactory = SelectConnectionFactory(options, crossTenant);
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var transaction = await connection.BeginTransactionAsync(options.IsolationLevel, cancellationToken).ConfigureAwait(false);

            if (options.ReadOnly)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    ReadOnlySql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            if (crossTenant)
                _logger.CrossTenantSessionOpened();
            else if (_rowLevelSecurity)
                await BindTenantAsync(connection, transaction, tenantId, cancellationToken).ConfigureAwait(false);

            return new DbSession(connection, transaction, owned: true, options.ReadOnly, tenantId, _commandTimeoutSeconds, _logger);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private IDbConnectionFactory SelectConnectionFactory(DbSessionOptions options, bool crossTenant)
    {
        if (crossTenant)
        {
            return _services.GetKeyedService<IDbConnectionFactory>(NpgsqlDataSourceKeys.CrossTenant)
                ?? throw new InvalidOperationException(
                    "A cross-tenant scope is active and row-level security is enabled, but no cross-tenant data "
                        + "source is configured. Set 'RowLevelSecurity:CrossTenantConnectionString' in the database's settings "
                        + "section ('SharedKernel:Persistence:{connection name}', or 'SharedKernel:Persistence:Npgsql' for an "
                        + "unnamed registration) to a role that is exempt from the tenant policy.");
        }

        if (options.ReadOnly && _services.GetKeyedService<IDbConnectionFactory>(NpgsqlDataSourceKeys.ReadOnly) is { } readOnly)
            return readOnly;

        return _services.GetRequiredService<IDbConnectionFactory>();
    }

    private Task BindTenantAsync(DbConnection connection, DbTransaction transaction, Guid? tenantId, CancellationToken cancellationToken)
    {
        var binder = _services.GetService<ITenantSessionBinder>()
            ?? throw new InvalidOperationException(
                "Row-level security is enabled but no ITenantSessionBinder is registered. Call "
                    + "'services.AddSharedKernelNpgsql(configuration, \"<connection name>\")' (AddSharedKernelPostgres does it).");

        return binder.BindAsync(connection, transaction, tenantId, cancellationToken);
    }
}
