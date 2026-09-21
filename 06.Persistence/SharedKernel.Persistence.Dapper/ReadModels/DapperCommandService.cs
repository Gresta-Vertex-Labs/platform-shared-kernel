using System.Diagnostics;
using Dapper;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Dapper.Diagnostics;
using SharedKernel.Persistence.Dapper.Options;

namespace SharedKernel.Persistence.Dapper.ReadModels;

/// <summary>
/// Abstract base class for Dapper-based write commands, enlisting in the ambient explicit
/// transaction (<see cref="IAmbientDbTransaction"/>) when one is active.
/// </summary>
/// <remarks>
/// <para>
/// When the caller's DI scope has an active <c>IUnitOfWork</c> transaction
/// (opened via <c>BeginTransactionAsync</c>/<c>ExecuteInTransactionAsync</c>), every method on this
/// class runs its command on THAT SAME connection and transaction — the write becomes part of the
/// same atomic unit as any EF Core-tracked changes saved within that transaction's scope, and rolls
/// back with it. Outside such a scope, each call opens, uses, and disposes its own connection,
/// auto-committing per statement (ordinary ADO.NET/Dapper behaviour for an un-transacted command).
/// </para>
/// <para>
/// <strong>No tenant protection of any kind — deliberately, not by oversight.</strong> This class
/// binds nothing to the database session and applies no tenant check of its own: it is the write-side
/// counterpart of <see cref="DapperReadService"/> (also unprotected), for a query/command against a
/// table that is either genuinely single-tenant or already enforced entirely at the database level
/// (e.g. row-level security with no defense-in-depth needed above it). A write against a
/// tenant-scoped table belongs on <see cref="TenantSafeDapperCommandService"/> instead, which fails
/// closed with no tenant resolved and no active <c>ICrossTenantScope</c>, exactly like its read
/// counterpart <see cref="TenantSafeDapperReadService"/>. Choosing this class for a tenant-scoped
/// table is a real gap — always prefer <see cref="TenantSafeDapperCommandService"/> there.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> String interpolation in SQL is forbidden — parameterized
/// commands only. Enforced by <c>00.Governance</c>'s SK0042 analyzer.
/// </para>
/// </remarks>
public abstract class DapperCommandService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAmbientDbTransaction? _ambientTransaction;
    private readonly int? _defaultCommandTimeoutSeconds;

    /// <summary>Initialises a new <see cref="DapperCommandService"/>.</summary>
    /// <param name="connectionFactory">
    /// Factory used to open a connection when no ambient transaction is active.
    /// </param>
    /// <param name="ambientTransaction">
    /// Optional. Resolved by DI when <c>EfCorePersistenceBuilder.Build()</c> registered it
    /// (always, with EF Core); <see langword="null"/> otherwise, in which case this service
    /// always opens its own connection.
    /// </param>
    /// <param name="options">Optional default command timeout — see <see cref="DapperReadService"/>'s constructor.</param>
    protected DapperCommandService(
        IDbConnectionFactory connectionFactory,
        IAmbientDbTransaction? ambientTransaction = null,
        DapperPersistenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
        _ambientTransaction = ambientTransaction;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeoutSeconds;
    }

    /// <summary>
    /// Executes a parameterized DML statement (INSERT, UPDATE, DELETE) and returns the number of
    /// affected rows.
    /// </summary>
    /// <param name="sql">The parameterized SQL statement. Must not use string interpolation.</param>
    /// <param name="parameters">
    /// Statement parameters (anonymous object or <see cref="DynamicParameters"/>), or
    /// <see langword="null"/> for statements with no parameters.
    /// </param>
    /// <param name="commandTimeout">
    /// Per-call command timeout (seconds), overriding the injected default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows affected by the statement.</returns>
    protected async Task<int> ExecuteAsync(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.Execute", ActivityKind.Client);

        if (_ambientTransaction?.Current is { } ambient)
        {
            var command = new CommandDefinition(
                sql, parameters, transaction: ambient.Transaction,
                commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
            return await ambient.Connection.ExecuteAsync(command);
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var standaloneCommand = new CommandDefinition(
            sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(standaloneCommand);
    }

    /// <summary>
    /// Executes a parameterized statement that returns a single scalar value (e.g. a
    /// database-generated identifier from <c>INSERT... RETURNING</c>).
    /// </summary>
    /// <typeparam name="TResult">The scalar result type.</typeparam>
    /// <param name="sql">The parameterized SQL statement. Must not use string interpolation.</param>
    /// <param name="parameters">Statement parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">See <see cref="ExecuteAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<TResult?> ExecuteScalarAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.ExecuteScalar", ActivityKind.Client);

        if (_ambientTransaction?.Current is { } ambient)
        {
            var command = new CommandDefinition(
                sql, parameters, transaction: ambient.Transaction,
                commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
            return await ambient.Connection.ExecuteScalarAsync<TResult>(command);
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var standaloneCommand = new CommandDefinition(
            sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<TResult>(standaloneCommand);
    }

    /// <summary>Gets whether this call is currently enlisted in an ambient explicit transaction.</summary>
    protected bool IsEnlistedInAmbientTransaction => _ambientTransaction?.Current is not null;

    private int? EffectiveTimeout(int? commandTimeout) => commandTimeout ?? _defaultCommandTimeoutSeconds;
}
