using SharedKernel.Domain;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;

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
/// <strong>Domain event dispatch (P-080, Cap 4):</strong> After <c>SaveChangesAsync</c> succeeds,
/// this class collects all domain events from tracked <see cref="IHasDomainEvents"/> entities,
/// calls <see cref="IDomainEventDispatcher.DispatchAsync"/> if a dispatcher is registered, then
/// clears domain events on each aggregate regardless of dispatcher registration (prevents
/// double-dispatch on subsequent saves).
/// </para>
/// <para>
/// <strong>Opt-in dispatcher:</strong> <c>IDomainEventDispatcher</c> is optional. If no
/// implementation is registered in DI, events are cleared but not dispatched. The consuming
/// service opts in by registering an <c>IDomainEventDispatcher</c> implementation alongside
/// the persistence builder.
/// </para>
/// <para>
/// <strong>Known trade-off:</strong> dispatch failure after a successful commit does not roll back
/// the committed transaction. Domain events raised in that save cycle are cleared and lost. This
/// is a deliberate design decision — the persistence boundary is the DbContext transaction;
/// event dispatch is a best-effort post-commit concern.
/// </para>
/// </remarks>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IDomainEventDispatcher? _dispatcher;

    /// <summary>
    /// Initialises a new <see cref="EfUnitOfWork"/> without a domain event dispatcher.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    public EfUnitOfWork(SharedKernelDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Initialises a new <see cref="EfUnitOfWork"/> with an optional domain event dispatcher.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="dispatcher">
    /// Optional dispatcher for domain events raised during the save cycle.
    /// When <see langword="null"/>, events are cleared but not dispatched.
    /// </param>
    public EfUnitOfWork(SharedKernelDbContext dbContext, IDomainEventDispatcher? dispatcher)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var result = await _dbContext.SaveChangesAsync(ct);

        // Collect all domain events from tracked aggregates post-commit.
        var aggregatesWithEvents = _dbContext.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var allEvents = aggregatesWithEvents
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Dispatch if a dispatcher is registered and there are events to dispatch.
        if (_dispatcher is not null && allEvents.Count > 0)
            await _dispatcher.DispatchAsync(allEvents, ct);

        // Always clear domain events — prevents double-dispatch on subsequent saves.
        foreach (var aggregate in aggregatesWithEvents)
            aggregate.ClearDomainEvents();

        return result;
    }
}
