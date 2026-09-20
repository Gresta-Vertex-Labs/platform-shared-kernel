using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.Diagnostics;
using SharedKernel.Persistence.Dapper.Options;

namespace SharedKernel.Persistence.Dapper.ReadModels;

/// <summary>
/// Abstract base class for Dapper-based read-side query services over a tenant-scoped table, binding
/// the current tenant to the database session for every query.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fails closed:</strong> when no tenant is resolved
/// (<see cref="ICurrentTenantContext.TenantId"/> is <see langword="null"/>) and no
/// <see cref="ICrossTenantScope"/> is active, every query method throws
/// <see cref="InvalidOperationException"/> before issuing any SQL — there is no "query runs with an
/// empty/wildcard tenant" fallback.
/// </para>
/// <para>
/// <strong>Defense in depth, not the enforcement mechanism.</strong> Binding
/// (<see cref="ITenantSessionBinder.BindAsync"/>) writes a session-local setting a matching
/// PostgreSQL row-level security policy reads — see
/// <c>SharedKernel.Persistence.PostgreSQL</c>'s <c>EnableTenantRowLevelSecurity</c> migration helper.
/// This class's own WHERE-clause discipline is real but is NOT what makes a WHERE-less query safe —
/// only the RLS policy is enforced against every possible statement, including a hand-written one
/// this class did not build. Deploy this class only against a table that also has that policy.
/// </para>
/// <para>
/// Because <c>set_config(..., is_local =&gt; true)</c> only resets automatically at the end of a
/// transaction, every query opens an explicit (read-only) transaction, binds the tenant inside it,
/// runs the query, and commits — a slightly heavier per-call cost than <see cref="DapperReadService"/>'s
/// plain connection-per-call shape, in exchange for a guarantee the session setting can never leak
/// into a later, differently-tenanted lease of a pooled connection.
/// </para>
/// </remarks>
public abstract class TenantSafeDapperReadService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantSessionBinder _tenantSessionBinder;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly ICrossTenantScope _crossTenantScope;
    private readonly ILogger _logger;
    private readonly int? _defaultCommandTimeoutSeconds;

    /// <summary>Initialises a new <see cref="TenantSafeDapperReadService"/>.</summary>
    protected TenantSafeDapperReadService(
        IDbConnectionFactory connectionFactory,
        ITenantSessionBinder tenantSessionBinder,
        ICurrentTenantContext tenantContext,
        ICrossTenantScope crossTenantScope,
        DapperPersistenceOptions? options = null,
        ILogger<TenantSafeDapperReadService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(tenantSessionBinder);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(crossTenantScope);

        _connectionFactory = connectionFactory;
        _tenantSessionBinder = tenantSessionBinder;
        _tenantContext = tenantContext;
        _crossTenantScope = crossTenantScope;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeoutSeconds;
        _logger = logger ?? NullLogger<TenantSafeDapperReadService>.Instance;
    }

    /// <summary>
    /// Executes a parameterized SELECT query, with the current tenant bound to the session for its
    /// duration, and returns every matching row.
    /// </summary>
    /// <typeparam name="TResult">The type to map each row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">Per-call command timeout (seconds).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every matching row, mapped to <typeparamref name="TResult"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// No tenant is resolved and no cross-tenant scope is active.
    /// </exception>
    protected async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var (tenantId, crossTenantActive) = RequireTenant();

        using var activity = DapperActivitySource.Source.StartActivity("Dapper.TenantSafeQuery", ActivityKind.Client);
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            await _tenantSessionBinder.BindAsync(connection, transaction, tenantId, crossTenantActive, cancellationToken);

            var command = new CommandDefinition(
                sql, parameters, transaction: transaction,
                commandTimeout: commandTimeout ?? _defaultCommandTimeoutSeconds, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<TResult>(command);

            transaction.Commit();
            return rows.AsList();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Executes a parameterized SELECT query, with the current tenant bound to the session for its
    /// duration, and returns a single result row, or <see langword="null"/> when no row matches.
    /// </summary>
    protected async Task<TResult?> QuerySingleOrDefaultAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var (tenantId, crossTenantActive) = RequireTenant();

        using var activity = DapperActivitySource.Source.StartActivity("Dapper.TenantSafeQuerySingleOrDefault", ActivityKind.Client);
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        try
        {
            await _tenantSessionBinder.BindAsync(connection, transaction, tenantId, crossTenantActive, cancellationToken);

            var command = new CommandDefinition(
                sql, parameters, transaction: transaction,
                commandTimeout: commandTimeout ?? _defaultCommandTimeoutSeconds, cancellationToken: cancellationToken);
            var result = await connection.QuerySingleOrDefaultAsync<TResult>(command);

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // Returns the tenant id to bind (or null when no tenant is resolved) together with whether an
    // active cross-tenant scope's RLS escape clause must also be bound. A null tenant id combined
    // with crossTenantActive: true is the admin/support shape — deliberately reading across every
    // tenant. Throws when neither a tenant nor an active cross-tenant scope is present — fail closed,
    // never a WHERE-less query against every tenant's rows by accident.
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
                + "A tenant-safe Dapper query refuses to run without one or the other.");
    }
}
