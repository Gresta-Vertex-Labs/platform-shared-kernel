using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// The one transaction of a dependency-injection scope, shared by the unit of work of every context resolved in
/// that scope.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every context commits.</strong> Each context the scope resolves registers here. When a unit of work
/// starts a transaction, every registered context that reaches the same database (same host, port, database and
/// user) is moved onto the transaction's connection (<c>SetDbConnection</c> + <c>UseTransaction</c>), and so is
/// every context resolved while it runs. Before the commit each of them with changes is saved; after it, each is
/// moved back to its own data source. A context that cannot join (another database, an open connection of its own,
/// a transaction of its own) is refused loudly at commit if it holds changes.
/// </para>
/// <para>
/// <strong>One transaction per scope.</strong> A unit of work called while a transaction is active — its own or
/// another context's — joins it. A joined operation that fails marks the transaction rollback-only.
/// </para>
/// <para>
/// <strong>Ambient transaction.</strong> The transaction is published on <see cref="AmbientDbTransactionAccessor"/>
/// for Dapper and the audit writer, and the previous value is restored when it ends — never cleared blindly.
/// </para>
/// </remarks>
internal sealed class UnitOfWorkCoordinator
{
    private const int MaxSavePasses = 16;

    private readonly List<SharedKernelDbContext> _contexts = [];
    private readonly AmbientDbTransactionAccessor? _ambient;
    private readonly ILogger _logger;
    private ActiveTransaction? _active;

    public UnitOfWorkCoordinator(AmbientDbTransactionAccessor? ambient = null, ILogger<UnitOfWorkCoordinator>? logger = null)
    {
        _ambient = ambient;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether a transaction is running in this scope.</summary>
    public bool IsActive => _active is not null;

    /// <summary>Registers a context resolved in this scope; it joins the running transaction immediately, if any.</summary>
    public void Track(SharedKernelDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_contexts.Contains(context))
            return;

        _contexts.Add(context);
        _active?.Enlist(context);
    }

    /// <summary>Queues a callback to run just before the commit of the running transaction.</summary>
    public void OnBeforeCommit(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (_active is null)
        {
            throw new InvalidOperationException(
                "OnBeforeCommit can only be called while ExecuteInTransactionAsync is running on this unit of work.");
        }

        _active.BeforeCommit.Add(callback);
    }

    /// <summary>Runs <paramref name="operation"/> in the scope's transaction, starting it on <paramref name="owner"/> if none runs.</summary>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        SharedKernelDbContext owner,
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(operation);

        Track(owner);

        if (_active is { } active)
            return await JoinAsync(active, owner, operation, cancellationToken).ConfigureAwait(false);

        var strategy = owner.Database.CreateExecutionStrategy();

        if (strategy.RetriesOnFailure && _contexts.FirstOrDefault(c => c.ChangeTracker.HasChanges()) is { } staged)
        {
            throw new InvalidOperationException(
                $"ExecuteInTransactionAsync was called with changes already staged on '{staged.GetType().Name}' while a " +
                "retrying execution strategy is configured. A retried attempt starts from a cleared change " +
                "tracker, so those changes would be committed on the first attempt but silently lost on a " +
                "retry. Stage every change inside the operation delegate, or call SaveChangesAsync first.");
        }

        var attempt = 0;

        return await strategy.ExecuteAsync(
            cancellationToken,
            async token =>
            {
                if (attempt++ > 0)
                    ClearTrackers();

                return await RunAttemptAsync(owner, operation, isolationLevel, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the changes of every context of the scope. One context with changes saves on its own (its own implicit
    /// transaction); several are saved in one transaction on <paramref name="owner"/>'s connection.
    /// </summary>
    public async Task<int> SaveChangesAsync(SharedKernelDbContext owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Track(owner);

        if (_active is { } active)
            return await active.SaveAllAsync(cancellationToken).ConfigureAwait(false);

        var others = _contexts.Where(c => !ReferenceEquals(c, owner) && c.ChangeTracker.HasChanges()).ToList();
        if (others.Count == 0)
            return await owner.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Several contexts: one transaction, saved without accepting changes so that a retried attempt saves the
        // same changes again, and accepted only after the commit (EF Core's documented multi-context retry pattern).
        var strategy = owner.Database.CreateExecutionStrategy();
        var written = await strategy.ExecuteAsync(
            cancellationToken,
            async token =>
            {
                var transaction = await owner.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    var attempt = Begin(owner, transaction);
                    var commitSent = false;
                    try
                    {
                        var total = await attempt.SaveAllAsync(token, acceptAllChanges: false).ConfigureAwait(false);
                        attempt.EnsureUncoordinatedContextsHaveNoChanges();

                        commitSent = true;
                        await CommitAsync(transaction, token).ConfigureAwait(false);
                        return total;
                    }
                    catch when (!commitSent)
                    {
                        await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                        throw;
                    }
                    finally
                    {
                        End(attempt);
                    }
                }
            }).ConfigureAwait(false);

        foreach (var context in _contexts)
            context.ChangeTracker.AcceptAllChanges();

        return written;
    }

    private async Task<TResult> RunAttemptAsync<TResult>(
        SharedKernelDbContext owner,
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken)
    {
        var transaction = isolationLevel.HasValue
            ? await owner.Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken).ConfigureAwait(false)
            : await owner.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            var attempt = Begin(owner, transaction);
            var commitSent = false;

            try
            {
                var result = await operation(cancellationToken).ConfigureAwait(false);

                if (result is IHasSuccessFlag { IsSuccess: false })
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    ClearTrackers();
                    return result;
                }

                if (attempt.RollbackOnly)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    ClearTrackers();
                    PersistenceLog.RolledBackAfterJoinedFailure(_logger, owner.GetType().Name);
                    throw new TransactionRolledBackException();
                }

                await attempt.SaveAllAsync(cancellationToken).ConfigureAwait(false);

                // Indexed loop: a callback may queue another callback.
                for (var i = 0; i < attempt.BeforeCommit.Count; i++)
                    await attempt.BeforeCommit[i](cancellationToken).ConfigureAwait(false);

                await attempt.SaveAllAsync(cancellationToken).ConfigureAwait(false);
                attempt.EnsureUncoordinatedContextsHaveNoChanges();

                commitSent = true;
                await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch
            {
                ClearTrackers();
                if (!commitSent)
                    await RollbackQuietlyAsync(transaction).ConfigureAwait(false);
                throw;
            }
            finally
            {
                End(attempt);
            }
        }
    }

    private static async Task<TResult> JoinAsync<TResult>(
        ActiveTransaction active,
        SharedKernelDbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        active.Enlist(context);

        TResult result;
        try
        {
            result = await operation(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            active.RollbackOnly = true;
            throw;
        }

        if (result is IHasSuccessFlag { IsSuccess: false })
        {
            active.RollbackOnly = true;
            return result;
        }

        await active.SaveAllAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // COMMIT: a server error means the transaction rolled back (known outcome, may be transient and retried); any
    // other failure — broken connection, timeout, cancellation — leaves the outcome unknown and must never be replayed.
    private static async Task CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsOutcomeUnknown(ex))
        {
            throw new CommitOutcomeUnknownException(ex);
        }
    }

    internal static bool IsOutcomeUnknown(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException)
                return false;
        }

        return true;
    }

    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection may be broken; disposing the transaction (or the server) rolls back.
        }
    }

    private ActiveTransaction Begin(SharedKernelDbContext owner, IDbContextTransaction transaction)
    {
        var connection = owner.Database.GetDbConnection();
        var active = new ActiveTransaction(this, owner, connection, transaction.GetDbTransaction());
        _active = active;

        if (_ambient is not null)
        {
            active.PreviousAmbient = _ambient.Current;
            _ambient.Current = (connection, active.DbTransaction);
        }

        foreach (var context in _contexts.ToList())
            active.Enlist(context);

        return active;
    }

    private void End(ActiveTransaction active)
    {
        active.Release();

        if (_ambient is not null)
            _ambient.Current = active.PreviousAmbient;

        if (ReferenceEquals(_active, active))
            _active = null;
    }

    private void ClearTrackers()
    {
        foreach (var context in _contexts)
        {
            try
            {
                context.ChangeTracker.Clear();
            }
            catch (ObjectDisposedException)
            {
                // A context disposed before its scope ended has nothing left to clear.
            }
        }
    }

    // Two connections reach the same database, as the same user, on the same side of the cross-tenant switch.
    internal static bool SameDatabase(DbConnection left, DbConnection right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (RowLevelSecurityConnections.IsCrossTenant(left) != RowLevelSecurityConnections.IsCrossTenant(right))
            return false;

        if (left is NpgsqlConnection && right is NpgsqlConnection)
        {
            var a = new NpgsqlConnectionStringBuilder(left.ConnectionString);
            var b = new NpgsqlConnectionStringBuilder(right.ConnectionString);
            return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
                && a.Port == b.Port
                && string.Equals(a.Database, b.Database, StringComparison.Ordinal)
                && string.Equals(a.Username, b.Username, StringComparison.Ordinal);
        }

        // Other providers (unit tests): only an identical, non in-memory connection string is the same database.
        return left.GetType() == right.GetType()
            && string.Equals(left.ConnectionString, right.ConnectionString, StringComparison.Ordinal)
            && !left.ConnectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ActiveTransaction(
        UnitOfWorkCoordinator coordinator,
        SharedKernelDbContext owner,
        DbConnection connection,
        DbTransaction dbTransaction)
    {
        private readonly List<SharedKernelDbContext> _enlisted = [owner];
        private readonly List<SharedKernelDbContext> _uncoordinated = [];

        public DbTransaction DbTransaction { get; } = dbTransaction;

        public List<Func<CancellationToken, Task>> BeforeCommit { get; } = [];

        public bool RollbackOnly { get; set; }

        public (DbConnection Connection, DbTransaction Transaction)? PreviousAmbient { get; set; }

        public void Enlist(SharedKernelDbContext context)
        {
            if (_enlisted.Contains(context) || _uncoordinated.Contains(context))
                return;

            var database = context.Database;
            var reason = CannotJoinReason(database);
            if (reason is not null)
            {
                _uncoordinated.Add(context);
                PersistenceLog.ContextCannotJoinTransaction(coordinator._logger, context.GetType().Name, owner.GetType().Name, reason);
                return;
            }

            database.SetDbConnection(connection, contextOwnsConnection: false);
            database.UseTransaction(DbTransaction);
            _enlisted.Add(context);
        }

        private string? CannotJoinReason(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database)
        {
            if (database.CurrentTransaction is not null)
                return "it runs a transaction of its own";

            var own = database.GetDbConnection();
            if (!SameDatabase(own, connection))
                return "it connects to another database or as another role";

            if (own.State != ConnectionState.Closed)
                return "its own connection is open";

            return null;
        }

        public async Task<int> SaveAllAsync(CancellationToken cancellationToken, bool acceptAllChanges = true)
        {
            var total = 0;
            for (var pass = 0; pass < MaxSavePasses; pass++)
            {
                var saved = false;

                // Snapshot: saving one context may dispatch domain events that resolve (and enlist) another.
                foreach (var context in _enlisted.ToList())
                {
                    if (!context.ChangeTracker.HasChanges())
                        continue;

                    total += await context.SaveChangesAsync(acceptAllChanges, cancellationToken).ConfigureAwait(false);
                    saved = true;
                }

                if (!saved || !acceptAllChanges)
                    return total;
            }

            throw new InvalidOperationException(
                $"The contexts of the unit of work still had changes after {MaxSavePasses} save passes; a domain-event "
                + "handler keeps changing another context.");
        }

        public void EnsureUncoordinatedContextsHaveNoChanges()
        {
            foreach (var context in _uncoordinated)
            {
                if (context.ChangeTracker.HasChanges())
                {
                    throw new InvalidOperationException(
                        $"'{context.GetType().Name}' has unsaved changes but cannot join the transaction of "
                        + $"'{owner.GetType().Name}' (it connects to another database or role, or runs its own "
                        + "connection or transaction). The transaction was rolled back instead of committing without "
                        + "them. Save that context through its own IUnitOfWork<TContext> outside this transaction.");
                }
            }
        }

        // Moves every joined context back to a connection of its own data source (created lazily on next use).
        public void Release()
        {
            foreach (var context in _enlisted)
            {
                if (ReferenceEquals(context, owner))
                    continue;

                try
                {
                    // Disposing EF Core's wrapper (created with transactionOwned: false) releases the connection
                    // reference UseTransaction took, without touching the owner's transaction or connection.
                    context.Database.CurrentTransaction?.Dispose();
                    context.Database.SetDbConnection(null, contextOwnsConnection: true);
                }
                catch (ObjectDisposedException)
                {
                    // Disposed before the scope ended; nothing to restore.
                }
            }
        }
    }
}
