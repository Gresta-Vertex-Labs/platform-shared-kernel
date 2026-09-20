using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// EF Core implementation of the unit-of-work commit boundary.
/// Delegates <see cref="SaveChangesAsync"/> to
/// <see cref="SharedKernelDbContext.SaveChangesAsync(CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the <strong>only</strong> permitted save boundary. Calling
/// <c>DbContext.SaveChangesAsync</c> directly anywhere outside this class is a hard violation
/// of the persistence architecture rules.
/// </para>
/// <para>
/// All three EF Core interceptors (Audit, SoftDelete, Concurrency) fire automatically within
/// this call before the database commit is issued.
/// </para>
/// <para>
/// <strong>Domain event dispatch:</strong> <see cref="SaveChangesAsync"/> now
/// dispatches domain events BEFORE the physical database save — see
/// <see cref="DomainEventDispatchLoop"/> for the full rationale (hard-delete event loss,
/// double-dispatch on handler failure, and dropped handler writes, all fixed by this reordering).
/// </para>
/// <para>
/// <strong>Opt-in dispatcher:</strong> <c>IDomainEventDispatcher</c> is optional. If no
/// implementation is registered in DI, events are cleared but not dispatched. The consuming
/// service opts in by registering an <c>IDomainEventDispatcher</c> implementation alongside
/// the persistence builder.
/// </para>
/// <para>
/// <strong>Single constructor rule:</strong> This class has exactly one public
/// constructor. <c>IDomainEventDispatcher?</c> is a nullable optional parameter resolved by the
/// DI container — DI resolves <see langword="null"/> when no implementation is registered and
/// resolves the registered implementation when present. Adding a second constructor is a hard
/// violation: the DI container may silently select the shorter constructor and skip the dispatcher.
/// </para>
/// <para>
/// <strong>05.Application bridge:</strong> this class no longer implements
/// <c>SharedKernel.Application.Behaviors.Transaction.IUnitOfWork</c> directly — that dual-interface
/// bridge moved to <c>13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence</c>,
/// which is legally positioned to reference both this package and <c>05.Application.Behaviors</c>. Its
/// adapter wraps this package's own <see cref="SharedKernel.Persistence.Abstractions.UnitOfWork.IUnitOfWork"/>
/// rather than casting to a concrete type — the fix for a confirmed pre-existing defect where the old
/// direct-cast registration threw <see cref="InvalidCastException"/> whenever
/// <c>WithTransactionalUnitOfWork()</c> was also enabled (the resolved instance was an
/// <see cref="EfTransactionalUnitOfWork"/>, which never implemented the dual interface).
/// </para>
/// <para>
/// <strong>Transient-fault retry-exhaustion logging:</strong>
/// <see cref="SaveChangesAsync"/> catches
/// <see cref="Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException"/> — the exception
/// type EF Core's own <c>ExecutionStrategy</c> throws (confirmed empirically) when the DbContext's
/// configured retrying execution strategy exhausts every attempt, wrapping the final underlying
/// failure as <see cref="Exception.InnerException"/> — logs a <c>TransientRetryExhausted</c>
/// Warning (EventId <c>6008</c>), then rethrows unchanged. Catching this specific exception TYPE,
/// rather than gating a broad <c>catch (Exception)</c> on <c>RetriesOnFailure</c>, is deliberate:
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>-derived conflicts (and
/// any other non-transient failure) are never retried by the execution strategy's own
/// <c>ShouldRetryOn</c> predicate, so they never produce a <c>RetryLimitExceededException</c>
/// wrapper — a broader catch would have misreported every concurrency conflict as a retry
/// exhaustion whenever retry happened to be configured. This has no effect when no retrying
/// execution strategy is configured at all — that shape of exception is never thrown in that case.
/// </para>
/// </remarks>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly ILogger<EfUnitOfWork> _logger;
    private readonly TransientFaultRetryOptions? _retryOptions;

    /// <summary>
    /// Initialises a new <see cref="EfUnitOfWork"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="dispatcher">
    /// Optional dispatcher for domain events raised during the save cycle.
    /// Resolved by DI as a nullable service — <see langword="null"/> when
    /// <c>IDomainEventDispatcher</c> is not registered; the concrete implementation when
    /// registered. Never supply a second constructor — see class remarks.
    /// </param>
    /// <param name="logger">
    /// Optional logger for the <c>TransientRetryExhausted</c> Warning (EventId <c>6008</c>).
    /// Resolved by DI when registered; falls back to <see cref="NullLogger{T}"/>
    /// otherwise.
    /// </param>
    /// <param name="retryOptions">
    /// Optional discoverability options registered by
    /// <c>EfCorePersistenceBuilder.WithTransientFaultRetry(...)</c>, consulted only to compute the
    /// <c>AttemptCount</c> value logged alongside <c>TransientRetryExhausted</c>
    /// (<c>MaxRetryCount + 1</c>). When <see langword="null"/> (retry was enabled solely via
    /// <c>UsePostgreSQL(..., maxRetryCount)</c> without also calling
    /// <c>.WithTransientFaultRetry()</c>), <c>AttemptCount</c> is logged as <c>1</c> — the exact
    /// configured count is not discoverable through any public EF Core API in that case.
    /// </param>
    public EfUnitOfWork(
        SharedKernelDbContext dbContext,
        IDomainEventDispatcher? dispatcher = null,
        ILogger<EfUnitOfWork>? logger = null,
        TransientFaultRetryOptions? retryOptions = null)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<EfUnitOfWork>.Instance;
        _retryOptions = retryOptions;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch runs BEFORE the physical save — see DomainEventDispatchLoop.
        await DomainEventDispatchLoop.RunAsync(_dbContext, _dispatcher, cancellationToken);

        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (RetryLimitExceededException ex)
        {
            PersistenceLog.TransientRetryExhausted(_logger, ex, (_retryOptions?.MaxRetryCount ?? 0) + 1);
            throw;
        }
    }
}
