using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.PostgreSQL.Diagnostics;

namespace SharedKernel.Persistence.PostgreSQL.MultiTenancy;

/// <summary>
/// <see cref="DbConnectionInterceptor"/> that binds the current tenant — and the
/// <see cref="ICrossTenantScope"/> escape clause — to every connection EF Core opens, and resets that
/// binding before the connection returns to the Npgsql pool.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why connection-scoped, not transaction-scoped:</strong> EF Core does not wrap every
/// operation in an explicit transaction — a single, non-transactional read is one statement with no
/// <c>BEGIN</c> at all. Binding only inside an explicit transaction (the shape
/// <c>SharedKernel.Persistence.Dapper</c>'s <c>TenantSafeDapperReadService</c> uses, since it always
/// opens one itself) would leave every plain query unprotected. Instead, this interceptor binds on
/// <see cref="ConnectionOpened"/>/<see cref="ConnectionOpenedAsync"/> — before any command can run on
/// the connection — and resets on <see cref="ConnectionClosing"/>/<see cref="ConnectionClosingAsync"/>
/// — before the connection is handed back to the pool — via
/// <see cref="ITenantSessionBinder.BindConnectionAsync"/>/<see cref="ITenantSessionBinder.ResetConnectionAsync"/>
/// (session-scoped <c>set_config(..., is_local =&gt; false)</c>). This covers every EF-issued command
/// on that connection lease, transactional or not.
/// </para>
/// <para>
/// <strong>Pooling safety:</strong> the reset step exists because a session-scoped setting otherwise
/// persists on the pooled PHYSICAL connection — without it, a later lease of the same physical
/// connection (by this same interceptor for a different tenant, OR by an entirely different consumer
/// of the same <c>NpgsqlDataSource</c>, e.g. a plain <c>IDbConnectionFactory</c>-based Dapper call)
/// could silently inherit a stale tenant binding. The bind step ALSO unconditionally overwrites
/// whatever was previously bound, so even if a reset were somehow skipped, the very next open on that
/// physical connection immediately re-binds the correct value before any command runs — reset is
/// defense in depth on top of that, not the only thing standing between leases.
/// </para>
/// <para>
/// <strong>Tenant resolution:</strong> read live from <c>eventData.Context</c> (never a
/// constructor-captured field, matching every other platform interceptor's pooling-safe pattern) cast
/// to <see cref="TenantedDbContext"/> — <see langword="null"/> when the context is not tenanted or is
/// unavailable, which binds an empty tenant (RLS then fails closed unless a cross-tenant scope is
/// active).
/// </para>
/// <para>
/// <strong>Sync path:</strong> <see cref="ConnectionOpened"/>/<see cref="ConnectionClosing"/> block on
/// the underlying async bind/reset call. This is the one place in the platform that accepts a
/// synchronous-over-asynchronous bridge: ADO.NET's connection-interceptor contract offers no
/// asynchronous alternative for a synchronous <c>Open()</c>/<c>Close()</c> caller, and the alternative
/// — leaving the sync path unprotected — is a security gap, not a performance trade-off worth taking.
/// Every repository method this platform ships uses the async EF Core surface; the sync path is only
/// reached by a consumer's own direct, synchronous <see cref="DbConnection"/> use.
/// </para>
/// <para>
/// Registered by <c>EfCorePersistenceBuilderRowLevelSecurityExtensions.WithRowLevelSecurity()</c> as
/// a process-lifetime singleton, via the same <c>IPersistenceOptionsExtension</c> seam
/// <c>SharedKernel.Persistence.EfCore.Encryption</c> uses for its own interceptors — applied
/// identically whether <c>WithDbContextPooling()</c> is enabled or not.
/// </para>
/// </remarks>
public sealed class RowLevelSecurityConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantSessionBinder _tenantSessionBinder;
    private readonly ICrossTenantScope _crossTenantScope;
    private readonly ILogger<RowLevelSecurityConnectionInterceptor> _logger;

    /// <summary>Initialises a new <see cref="RowLevelSecurityConnectionInterceptor"/>.</summary>
    public RowLevelSecurityConnectionInterceptor(
        ITenantSessionBinder tenantSessionBinder,
        ICrossTenantScope crossTenantScope,
        ILogger<RowLevelSecurityConnectionInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantSessionBinder);
        ArgumentNullException.ThrowIfNull(crossTenantScope);
        ArgumentNullException.ThrowIfNull(logger);

        _tenantSessionBinder = tenantSessionBinder;
        _crossTenantScope = crossTenantScope;
        _logger = logger;
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        BindAsync(connection, eventData, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await BindAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ResetAsync(connection).GetAwaiter().GetResult();
        return base.ConnectionClosing(connection, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await ResetAsync(connection).ConfigureAwait(false);
        return await base.ConnectionClosingAsync(connection, eventData, result).ConfigureAwait(false);
    }

    private Task BindAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken)
    {
        var tenantId = (eventData.Context as TenantedDbContext)?.CurrentTenant.TenantId;
        return _tenantSessionBinder.BindConnectionAsync(connection, tenantId, _crossTenantScope.IsActive, cancellationToken);
    }

    // Deliberately CancellationToken.None: resetting must still run even when the operation that
    // triggered the close was itself cancelled — an already-cancelled token would abort the reset and
    // leave a stale binding on the pooled connection. Failures are logged, never rethrown, so a reset
    // problem cannot prevent the connection from actually closing.
    private async Task ResetAsync(DbConnection connection)
    {
        try
        {
            await _tenantSessionBinder.ResetConnectionAsync(connection, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.RowLevelSecurityResetFailed(exception);
        }
    }
}
