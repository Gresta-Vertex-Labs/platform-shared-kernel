using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Execution.Transactions;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// The unit of work of one specific context. Inject it when a service registers more than one context and
/// wants to start the transaction on a particular one; otherwise inject the plain <see cref="IUnitOfWork"/>,
/// which resolves to the first registered context.
/// </summary>
/// <typeparam name="TContext">The context this unit of work starts transactions on.</typeparam>
/// <remarks>
/// <para>
/// Every unit of work of a scope shares one transaction: whichever starts it, every context of the scope that
/// reaches the same database joins it and is saved before the commit (see <see cref="IUnitOfWork"/>).
/// </para>
/// <para>
/// Also resolvable as a keyed <see cref="IUnitOfWork"/> whose key is <c>typeof(TContext)</c>:
/// <c>[FromKeyedServices(typeof(OrderDbContext))] IUnitOfWork unitOfWork</c>.
/// </para>
/// </remarks>
public interface IUnitOfWork<TContext> : IUnitOfWork
    where TContext : SharedKernelDbContext
{
}

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> for one context — the single commit boundary.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Transactions run inside the execution strategy.</strong> <see cref="ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},IsolationLevel?,CancellationToken)"/>
/// runs the whole delegate per attempt, so the default Npgsql retry and explicit transactions coexist: a
/// transient failure replays the delegate. An attempt begins the transaction on this context, moves every other
/// context of the scope on the same database onto it, publishes it on <see cref="IAmbientDbTransaction"/> (Dapper
/// and the audit writer enlist), runs the operation, saves every context with changes (each dispatches its domain
/// events first), runs the <see cref="OnBeforeCommit"/> callbacks (saving again if they staged changes) and
/// commits. A failed <c>Result</c> or an exception rolls back and clears the change trackers.
/// </para>
/// <para>
/// <strong>Retry safety:</strong> the trackers are cleared before each retried attempt, never before the first.
/// Under a retrying strategy, changes staged before the call would be lost on a retry, so the call refuses to
/// start in that case. A <c>COMMIT</c> that fails without a server response is never retried
/// (<see cref="CommitOutcomeUnknownException"/>).
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Mirrors IUnitOfWork's overload set.
internal sealed class EfUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : SharedKernelDbContext
{
    private readonly TContext _dbContext;
    private readonly ILogger _logger;
    private readonly UnitOfWorkCoordinator _coordinator;

    public EfUnitOfWork(
        TContext dbContext,
        ILogger<EfUnitOfWork<TContext>>? logger = null,
        IAmbientDbTransaction? ambientTransaction = null,
        UnitOfWorkCoordinator? coordinator = null)
        : this(dbContext, (ILogger?)logger ?? NullLogger.Instance, ambientTransaction, coordinator)
    {
    }

    internal EfUnitOfWork(TContext dbContext, ILogger logger, IAmbientDbTransaction? ambientTransaction, UnitOfWorkCoordinator? coordinator = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
        _logger = logger;
        _coordinator = coordinator ?? new UnitOfWorkCoordinator(ambientTransaction as AmbientDbTransactionAccessor);
        _coordinator.Track(dbContext);
    }

    /// <inheritdoc />
    public bool IsTransactionActive => _coordinator.IsActive;

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        WithRetryExhaustionLoggingAsync(() => _coordinator.SaveChangesAsync(_dbContext, cancellationToken), ConfiguredAttemptCount);

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteInTransactionAsync<object?>(
            async token =>
            {
                await operation(token);
                return null;
            },
            isolationLevel,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(operation, isolationLevel: null, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        IsolationLevel? isolationLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return _coordinator.IsActive
            ? _coordinator.ExecuteInTransactionAsync(_dbContext, operation, isolationLevel, cancellationToken)
            : WithRetryExhaustionLoggingAsync(
                () => _coordinator.ExecuteInTransactionAsync(_dbContext, operation, isolationLevel, cancellationToken),
                ConfiguredAttemptCount);
    }

    /// <inheritdoc />
    public void OnBeforeCommit(Func<CancellationToken, Task> callback) => _coordinator.OnBeforeCommit(callback);

    private int ConfiguredAttemptCount()
        => (_dbContext.GetService<IDbContextOptions>()
            .FindExtension<PostgresConventionsOptionsExtension>()?.MaxRetryCount ?? 0) + 1;

    // Only RetryLimitExceededException: a concurrency conflict or any other non-transient failure is never
    // retried, so a broader catch would misreport it as a retry exhaustion.
    private async Task<TResult> WithRetryExhaustionLoggingAsync<TResult>(Func<Task<TResult>> operation, Func<int> attemptCount)
    {
        try
        {
            return await operation();
        }
        catch (RetryLimitExceededException ex)
        {
            PersistenceLog.TransientRetryExhausted(_logger, ex, attemptCount());
            throw;
        }
    }
}
#pragma warning restore RS0026

/// <summary>Non-generic construction helper for code that holds a context of a statically unknown type (tests, tools).</summary>
internal sealed class EfUnitOfWork
{
    private EfUnitOfWork()
    {
    }

    /// <summary>
    /// Creates the unit of work of <paramref name="context"/>, attaching <paramref name="dispatcher"/> to the
    /// context (domain events are dispatched by the context itself before each save).
    /// </summary>
    public static EfUnitOfWork<TContext> For<TContext>(
        TContext context,
        SharedKernel.Domain.Abstractions.IDomainEventDispatcher? dispatcher = null,
        ILogger? logger = null,
        IAmbientDbTransaction? ambientTransaction = null,
        UnitOfWorkCoordinator? coordinator = null)
        where TContext : SharedKernelDbContext
    {
        context.AttachLease(context.RequestContext, dispatcher, context.CrossTenantScope);
        return new EfUnitOfWork<TContext>(context, logger ?? NullLogger.Instance, ambientTransaction, coordinator);
    }
}
