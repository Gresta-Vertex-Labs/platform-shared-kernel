using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Binds the caller's tenant to every command EF Core runs, transaction-locally — never for the session.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// <strong>Inside a transaction</strong> (the unit of work, or the transaction every <c>SaveChanges</c> gets under
/// row-level security — see <see cref="RowLevelSecuritySaveChangesInterceptor"/>) the tenant is bound once, by the
/// first command, and again only when the tenant changes or a savepoint rollback may have undone the binding. A
/// query carries the binding in its own round trip (the command text is prefixed with
/// <see cref="TenantSessionSql.BindStatement"/>); a <c>SaveChanges</c> batch gets a separate
/// <c>set_config</c> command first, because EF Core checks the row count of each statement of the batch by
/// position.
/// </description></item>
/// <item><description>
/// <strong>Outside a transaction</strong> (a plain query, <c>ExecuteUpdate</c>, raw SQL) every command is
/// prefixed: the statements of one command run in a single implicit transaction, so the binding covers exactly
/// that command. The prefix is a <c>DO</c> block, which returns no result set, so readers, scalars and row counts
/// are unchanged.
/// </description></item>
/// </list>
/// <para>
/// A physical connection therefore returns to the pool — or to a transaction-mode PgBouncer — with no tenant
/// attached. Migration commands are left alone (they run as the migration role, and some DDL cannot run in a
/// transaction).
/// </para>
/// <para>
/// <strong>Cross-tenant work.</strong> The application role never sees another tenant's rows. While an
/// <see cref="ICrossTenantScope"/> is active, commands must run on the cross-tenant role's connection
/// (<see cref="RowLevelSecurityDatabaseFacadeExtensions.UseCrossTenantConnection"/>); a command on the application
/// connection inside a scope, or on the cross-tenant connection outside one, is refused.
/// </para>
/// </remarks>
internal sealed class RowLevelSecurityCommandInterceptor : DbCommandInterceptor
{
    private const string BindSql = "SELECT set_config('" + TenantSessionSql.TenantIdSetting + "', @sk_tenant_id, true)";

    private readonly ICrossTenantScope _crossTenantScope;

    public RowLevelSecurityCommandInterceptor(ICrossTenantScope crossTenantScope)
    {
        ArgumentNullException.ThrowIfNull(crossTenantScope);
        _crossTenantScope = crossTenantScope;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        BindAsync(command, eventData, async: false, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await BindAsync(command, eventData, async: true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        BindAsync(command, eventData, async: false, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await BindAsync(command, eventData, async: true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        BindAsync(command, eventData, async: false, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await BindAsync(command, eventData, async: true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // With async: false every await below completes synchronously, so the sync overloads never block on I/O.
    private async ValueTask BindAsync(DbCommand command, CommandEventData eventData, bool async, CancellationToken cancellationToken)
    {
        if (eventData.CommandSource == CommandSource.Migrations || eventData.Context is not { } context)
            return;

        var connection = command.Connection
            ?? throw new InvalidOperationException("The command has no connection.");

        var onCrossTenantConnection = RowLevelSecurityConnections.IsCrossTenant(connection);
        var scopeActive = _crossTenantScope.IsActive;

        if (scopeActive && !onCrossTenantConnection)
        {
            throw new InvalidOperationException(
                "A cross-tenant scope is active, but this context runs on the application database role, "
                    + "which row-level security limits to one tenant. Call "
                    + "'context.Database.UseCrossTenantConnection()' on a new, non-pooled context before its "
                    + "first query, or use a Dapper session, which switches automatically.");
        }

        if (!scopeActive && onCrossTenantConnection)
        {
            throw new InvalidOperationException(
                "This context runs on the cross-tenant database role, but no cross-tenant scope is active. "
                    + "Dispose the context when the cross-tenant work ends.");
        }

        if (onCrossTenantConnection)
            return; // The cross-tenant role is not limited by the tenant policy; nothing to bind.

        var tenantId = (context as SharedKernelDbContext)?.RequestContext.TenantId;
        var isSaveChanges = eventData.CommandSource == CommandSource.SaveChanges;

        if (command.Transaction is { } transaction)
        {
            var transactionId = context.Database.CurrentTransaction?.TransactionId;
            if (transactionId is { } id && !RowLevelSecurityBindings.TryMarkBound(context, id, tenantId))
                return; // Already bound in this transaction, for this tenant.

            if (isSaveChanges)
            {
                await ExecuteBindAsync(connection, transaction, tenantId, async, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        else if (isSaveChanges)
        {
            throw new InvalidOperationException(
                "SaveChanges ran without a transaction on a context with row-level security, so the tenant could "
                    + "not be bound. Do not set AutoTransactionBehavior to Never on such a context.");
        }

        command.CommandText = TenantSessionSql.BindStatement(tenantId) + command.CommandText;
    }

    private static async ValueTask ExecuteBindAsync(
        DbConnection connection, DbTransaction transaction, Guid? tenantId, bool async, CancellationToken cancellationToken)
    {
        await using var bind = connection.CreateCommand();
        bind.Transaction = transaction;
        bind.CommandText = BindSql;

        var parameter = bind.CreateParameter();
        parameter.ParameterName = "sk_tenant_id";
        parameter.Value = tenantId?.ToString() ?? string.Empty;
        bind.Parameters.Add(parameter);

        if (async)
            await bind.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        else
            bind.ExecuteNonQuery();
    }
}

/// <summary>
/// Makes every <c>SaveChanges</c> of a row-level-security context run in a transaction
/// (<see cref="AutoTransactionBehavior.Always"/>), so its batch can be preceded by the tenant binding. EF Core
/// otherwise skips the transaction for a single-statement save.
/// </summary>
internal sealed class RowLevelSecuritySaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RequireTransaction(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        RequireTransaction(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void RequireTransaction(DbContext? context)
    {
        if (context is not null && context.Database.AutoTransactionBehavior == AutoTransactionBehavior.WhenNeeded)
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }
}

/// <summary>
/// What each context last bound inside which EF Core transaction, so a transaction is bound once rather than on
/// every command.
/// </summary>
/// <remarks>
/// Keyed by the context and EF Core's per-transaction <c>TransactionId</c>, never by the
/// <see cref="DbTransaction"/> object, which Npgsql reuses across transactions of the same connection.
/// </remarks>
internal static class RowLevelSecurityBindings
{
    private static readonly ConditionalWeakTable<DbContext, Binding> Bindings = new();

    /// <summary>
    /// Records that <paramref name="tenantId"/> is bound in <paramref name="transactionId"/>. Returns
    /// <see langword="false"/> when that exact binding was already recorded (nothing to do).
    /// </summary>
    public static bool TryMarkBound(DbContext context, Guid transactionId, Guid? tenantId)
    {
        var binding = Bindings.GetOrCreateValue(context);
        lock (binding)
        {
            if (binding.TransactionId == transactionId && binding.TenantId == tenantId)
                return false;

            binding.TransactionId = transactionId;
            binding.TenantId = tenantId;
            return true;
        }
    }

    /// <summary>Forgets the context's binding, so the next command binds again.</summary>
    public static void Forget(DbContext context)
    {
        if (Bindings.TryGetValue(context, out var binding))
        {
            lock (binding)
            {
                binding.TransactionId = null;
                binding.TenantId = null;
            }
        }
    }

    private sealed class Binding
    {
        public Guid? TransactionId { get; set; }

        public Guid? TenantId { get; set; }
    }
}

/// <summary>
/// Forgets a context's tenant binding when a savepoint is rolled back: the rollback also undoes a
/// <c>set_config</c> made after the savepoint, so the next command must bind again.
/// </summary>
internal sealed class RowLevelSecurityTransactionInterceptor : DbTransactionInterceptor
{
    public override void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData)
    {
        if (eventData.Context is { } context)
            RowLevelSecurityBindings.Forget(context);
    }

    public override Task RolledBackToSavepointAsync(
        DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            RowLevelSecurityBindings.Forget(context);

        return Task.CompletedTask;
    }
}
