using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// <see cref="DbCommandInterceptor"/> that re-binds the current tenant — and the
/// <see cref="ICrossTenantScope"/> escape clause — to the ACTIVE EXPLICIT TRANSACTION immediately
/// before every command EF Core executes on it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists alongside <see cref="RowLevelSecurityConnectionInterceptor"/>:</strong>
/// that interceptor binds once, session-scoped, when a connection opens. For the common case — a
/// connection opened, used for one statement, and closed again — that is already exact, because the
/// NEXT statement gets a fresh <see cref="RowLevelSecurityConnectionInterceptor.ConnectionOpened"/>
/// bind reflecting whatever is current at THAT moment. It stops being exact the moment a connection's
/// lease spans more than one statement under an EXPLICIT transaction: the tenant/cross-tenant setting
/// bound once at connection-open time does not move when an <see cref="ICrossTenantScope"/> is entered
/// or exited PARTWAY through that transaction, so a later statement on the same still-open transaction
/// could run under a stale binding — either silently permitting a write the caller's CURRENT state no
/// longer authorizes, or silently rejecting one it now does.
/// </para>
/// <para>
/// This interceptor closes that gap by re-issuing the SAME <see cref="ITenantSessionBinder.BindAsync"/>
/// transaction-scoped (<c>SET LOCAL</c>-equivalent, self-resetting) bind immediately before EVERY
/// command that runs inside an explicit <see cref="System.Data.Common.DbTransaction"/> — reading
/// <see cref="ICrossTenantScope.IsActive"/> and the current tenant LIVE, at the moment each statement
/// is about to run, never relying on whatever a previous statement on the same transaction bound.
/// Outside an explicit transaction (<c>command.Transaction is null</c>), nothing is re-bound here —
/// the connection-scoped bind, refreshed on the next open/close cycle
/// <see cref="RowLevelSecurityConnectionInterceptor"/> already performs, is exact for that shape, and
/// re-binding on every untransacted statement would only add redundant round trips.
/// </para>
/// <para>
/// <strong>Not a substitute for <see cref="RowLevelSecurityConnectionInterceptor"/>:</strong> the
/// connection-scoped bind/reset it performs remains the defense-in-depth backstop for any command that
/// does not go through this interceptor at all — most notably a Dapper statement sharing this
/// context's connection outside an explicit transaction. The two interceptors are always registered
/// together by <c>EfCorePersistenceBuilderRowLevelSecurityExtensions.WithRowLevelSecurity()</c>.
/// </para>
/// <para>
/// <strong>Deliberate cost:</strong> two extra <c>set_config</c> round trips before every command that
/// runs inside an explicit transaction, even when nothing has actually changed since the last one on
/// that same transaction. Chosen over a "skip when unchanged" cache for this first version because it
/// is trivially, structurally correct — a cache keyed on the wrong lifetime is exactly the class of bug
/// this interceptor exists to close. Only services that opt into <c>.WithRowLevelSecurity()</c> AND use
/// explicit, multi-statement transactions pay this cost at all.
/// </para>
/// </remarks>
public sealed class RowLevelSecurityCommandInterceptor : DbCommandInterceptor
{
    private readonly ITenantSessionBinder _tenantSessionBinder;
    private readonly ICrossTenantScope _crossTenantScope;

    /// <summary>Initialises a new <see cref="RowLevelSecurityCommandInterceptor"/>.</summary>
    public RowLevelSecurityCommandInterceptor(ITenantSessionBinder tenantSessionBinder, ICrossTenantScope crossTenantScope)
    {
        ArgumentNullException.ThrowIfNull(tenantSessionBinder);
        ArgumentNullException.ThrowIfNull(crossTenantScope);

        _tenantSessionBinder = tenantSessionBinder;
        _crossTenantScope = crossTenantScope;
    }

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RebindAsync(command, eventData, CancellationToken.None).GetAwaiter().GetResult();
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RebindAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        RebindAsync(command, eventData, CancellationToken.None).GetAwaiter().GetResult();
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RebindAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        RebindAsync(command, eventData, CancellationToken.None).GetAwaiter().GetResult();
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await RebindAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    // No-op outside an explicit transaction — see the class remarks for why the connection-scoped
    // bind already covers that shape exactly. Reads tenant identity and the cross-tenant escape LIVE,
    // off eventData.Context and _crossTenantScope, never a cached/captured value.
    private Task RebindAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        if (command.Transaction is null || command.Connection is null)
            return Task.CompletedTask;

        var tenantId = (eventData.Context as TenantedDbContext)?.CurrentTenantId;
        return _tenantSessionBinder.BindAsync(
            command.Connection, command.Transaction, tenantId, _crossTenantScope.IsActive, cancellationToken);
    }
}
