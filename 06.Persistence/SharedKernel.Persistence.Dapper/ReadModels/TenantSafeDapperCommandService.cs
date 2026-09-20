using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Dapper.Diagnostics;
using SharedKernel.Persistence.Dapper.Options;

namespace SharedKernel.Persistence.Dapper.ReadModels;

/// <summary>
/// Abstract base class for Dapper-based write commands over a tenant-scoped table, binding the
/// current tenant — and, when active, the <see cref="ICrossTenantScope"/> escape clause — to the
/// database session for every statement. The write-side counterpart of
/// <see cref="TenantSafeDapperReadService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fails closed:</strong> exactly like <see cref="TenantSafeDapperReadService"/> — when no
/// tenant is resolved (<see cref="ICurrentTenantContext.TenantId"/> is <see langword="null"/>) and no
/// <see cref="ICrossTenantScope"/> is active, every command method throws
/// <see cref="InvalidOperationException"/> before issuing any SQL.
/// </para>
/// <para>
/// <strong>Defense in depth, not the enforcement mechanism</strong> — see
/// <see cref="TenantSafeDapperReadService"/>'s own remarks. Deploy this class only against a table
/// that also carries a matching PostgreSQL row-level security policy.
/// </para>
/// <para>
/// <strong>Ambient-transaction enlistment, tenant bound per statement:</strong> when the caller's DI
/// scope has an active <c>ITransactionalUnitOfWork</c> transaction, every method here runs its
/// statement on that SAME connection/transaction — exactly like <see cref="DapperCommandService"/> —
/// but ALSO rebinds the tenant/cross-tenant session setting, via <c>SET LOCAL</c>-equivalent
/// transaction-scoped <see cref="ITenantSessionBinder.BindAsync"/>, immediately before that specific
/// statement runs. This is deliberate, not redundant: the ambient transaction may already be open
/// (started before this call), so whatever an EF Core command most recently bound on it could be
/// stale by the time this statement executes — e.g. an <see cref="ICrossTenantScope"/> entered or
/// exited between the transaction's start and this call. Rebinding live, right before the statement
/// that actually needs the setting, is what keeps an enlisted Dapper write correct regardless of when,
/// or whether, anything else touched the shared transaction.
/// </para>
/// <para>
/// Outside an ambient transaction, each call opens its own connection and a dedicated (read-write)
/// transaction — required because <c>set_config(..., is_local =&gt; true)</c> only resets
/// automatically at the end of a transaction — binds the tenant inside it, runs the statement, and
/// commits, mirroring <see cref="TenantSafeDapperReadService"/>'s own per-call transaction shape.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> String interpolation in SQL is forbidden — parameterized
/// commands only. Enforced by <c>00.Governance</c>'s SK0042 analyzer.
/// </para>
/// </remarks>
public abstract class TenantSafeDapperCommandService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantSessionBinder _tenantSessionBinder;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly ICrossTenantScope _crossTenantScope;
    private readonly IAmbientDbTransaction? _ambientTransaction;
    private readonly int? _defaultCommandTimeoutSeconds;
    private readonly ILogger _logger;

    /// <summary>Initialises a new <see cref="TenantSafeDapperCommandService"/>.</summary>
    /// <param name="connectionFactory">Factory used to open a connection when no ambient transaction is active.</param>
    /// <param name="tenantSessionBinder">Binds the current tenant/cross-tenant state to the database session.</param>
    /// <param name="tenantContext">Resolves the current tenant identity.</param>
    /// <param name="crossTenantScope">Reports whether an explicit cross-tenant bypass is active.</param>
    /// <param name="ambientTransaction">
    /// Optional. Resolved by DI when <c>EfCorePersistenceBuilder.WithTransactionalUnitOfWork()</c> was
    /// called for this scope; <see langword="null"/> otherwise, in which case this service always
    /// opens its own connection and transaction.
    /// </param>
    /// <param name="options">Optional default command timeout.</param>
    /// <param name="logger">Optional logger for the rejected-call warning.</param>
    protected TenantSafeDapperCommandService(
        IDbConnectionFactory connectionFactory,
        ITenantSessionBinder tenantSessionBinder,
        ICurrentTenantContext tenantContext,
        ICrossTenantScope crossTenantScope,
        IAmbientDbTransaction? ambientTransaction = null,
        DapperPersistenceOptions? options = null,
        ILogger<TenantSafeDapperCommandService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(tenantSessionBinder);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(crossTenantScope);

        _connectionFactory = connectionFactory;
        _tenantSessionBinder = tenantSessionBinder;
        _tenantContext = tenantContext;
        _crossTenantScope = crossTenantScope;
        _ambientTransaction = ambientTransaction;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeoutSeconds;
        _logger = logger ?? NullLogger<TenantSafeDapperCommandService>.Instance;
    }

    /// <summary>
    /// Executes a parameterized DML statement (INSERT, UPDATE, DELETE), with the current tenant bound
    /// to the session for its duration, and returns the number of affected rows.
    /// </summary>
    /// <param name="sql">The parameterized SQL statement. Must not use string interpolation.</param>
    /// <param name="parameters">
    /// Statement parameters (anonymous object or <see cref="DynamicParameters"/>), or
    /// <see langword="null"/> for statements with no parameters.
    /// </param>
    /// <param name="commandTimeout">Per-call command timeout (seconds), overriding the injected default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows affected by the statement.</returns>
    /// <exception cref="InvalidOperationException">
    /// No tenant is resolved and no cross-tenant scope is active.
    /// </exception>
    protected async Task<int> ExecuteAsync(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var (tenantId, crossTenantActive) = RequireTenant();

        using var activity = DapperActivitySource.Source.StartActivity("Dapper.TenantSafeExecute", ActivityKind.Client);

        if (_ambientTransaction?.Current is { } ambient)
        {
            await _tenantSessionBinder.BindAsync(
                ambient.Connection, ambient.Transaction, tenantId, crossTenantActive, cancellationToken);

            var command = new CommandDefinition(
                sql, parameters, transaction: ambient.Transaction,
                commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
            return await ambient.Connection.ExecuteAsync(command);
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            await _tenantSessionBinder.BindAsync(connection, transaction, tenantId, crossTenantActive, cancellationToken);

            var standaloneCommand = new CommandDefinition(
                sql, parameters, transaction: transaction,
                commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
            var affected = await connection.ExecuteAsync(standaloneCommand);

            transaction.Commit();
            return affected;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Executes a parameterized statement that returns a single scalar value (e.g. a
    /// database-generated identifier from <c>INSERT... RETURNING</c>), with the current tenant bound
    /// to the session for its duration.
    /// </summary>
    /// <typeparam name="TResult">The scalar result type.</typeparam>
    /// <param name="sql">The parameterized SQL statement. Must not use string interpolation.</param>
    /// <param name="parameters">Statement parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">See <see cref="ExecuteAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// No tenant is resolved and no cross-tenant scope is active.
    /// </exception>
    protected async Task<TResult?> ExecuteScalarAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var (tenantId, crossTenantActive) = RequireTenant();

        using var activity = DapperActivitySource.Source.StartActivity("Dapper.TenantSafeExecuteScalar", ActivityKind.Client);

        if (_ambientTransaction?.Current is { } ambient)
        {
            await _tenantSessionBinder.BindAsync(
                ambient.Connection, ambient.Transaction, tenantId, crossTenantActive, cancellationToken);

            var command = new CommandDefinition(
                sql, parameters, transaction: ambient.Transaction,
                commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
            return await ambient.Connection.ExecuteScalarAsync<TResult>(command);
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            await _tenantSessionBinder.BindAsync(connection, transaction, tenantId, crossTenantActive, cancellationToken);

            var standaloneCommand = new CommandDefinition(
                sql, parameters, transaction: transaction,
                commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
            var result = await connection.ExecuteScalarAsync<TResult>(standaloneCommand);

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Gets whether this call is currently enlisted in an ambient explicit transaction.</summary>
    protected bool IsEnlistedInAmbientTransaction => _ambientTransaction?.Current is not null;

    // Returns the tenant id to bind (or null when no tenant is resolved) together with whether an
    // active cross-tenant scope's RLS escape clause must also be bound. Throws when neither a tenant
    // nor an active cross-tenant scope is present — fail closed, never a WHERE-less write against
    // every tenant's rows by accident. Mirrors TenantSafeDapperReadService.RequireTenant exactly.
    private (Guid? TenantId, bool CrossTenantActive) RequireTenant()
    {
        var crossTenantActive = _crossTenantScope.IsActive;

        if (_tenantContext.TenantId is { } tenantId)
            return (tenantId, crossTenantActive);

        if (crossTenantActive)
            return (null, true);

        _logger.TenantSafeCallRejectedNoTenant();
        throw new InvalidOperationException(
            "No tenant is resolved for the current call, and no cross-tenant scope is active. "
                + "A tenant-safe Dapper command refuses to run without one or the other.");
    }

    private int? EffectiveTimeout(int? commandTimeout) => commandTimeout ?? _defaultCommandTimeoutSeconds;
}
