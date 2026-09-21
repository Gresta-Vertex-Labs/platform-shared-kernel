using SharedKernel.Application.Transactions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;

#pragma warning disable RS0026 // UpdateAsync has an expected-version overload; both keep the optional token last.

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Write-side repository for an aggregate root: loads aggregates <strong>with change tracking</strong> so their
/// changes are saved, and stages inserts, updates and deletes for the unit of work.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>Registration.</b> Registered automatically for every aggregate root the service's DbContexts map; inject
/// <c>IRepository&lt;Order, OrderId&gt;</c> without writing a class.
/// </para>
/// <para>
/// <b>Tracked and untracked members.</b> <see cref="GetByIdAsync"/>, <see cref="FirstOrDefaultAsync"/> and
/// <see cref="ListAsync"/> are redeclared here and return <em>tracked</em> aggregates: change them through their
/// domain methods and call <see cref="IUnitOfWork.SaveChangesAsync"/>, no <c>UpdateAsync</c> needed. Every other
/// member inherited from <see cref="IReadRepository{TAggregate, TId}"/> (counts, projections, pages, streams)
/// stays untracked. Called through an <see cref="IReadRepository{TAggregate, TId}"/> reference, the same object
/// answers untracked.
/// </para>
/// <para>
/// <b>Saving.</b> Nothing here writes to the database; changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/> or inside <c>IUnitOfWork.ExecuteInTransactionAsync</c>.
/// </para>
/// </remarks>
public interface IRepository<TAggregate, TId> : IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Returns the tracked aggregate with the given identity, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="id">The aggregate's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete aggregate, tracked; or <see langword="null"/>.</returns>
    /// <remarks>Loads the whole aggregate, like <see cref="IReadRepository{TAggregate, TId}.GetByIdAsync"/>.</remarks>
    new Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Returns the first tracked aggregate matching <paramref name="spec"/>, or <see langword="null"/>.</summary>
    /// <param name="spec">
    /// The query. To load a soft-deleted aggregate (for example to restore it), set
    /// <see cref="ISpecification{T}.IncludeDeleted"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first match, tracked; or <see langword="null"/>.</returns>
    new Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Returns every aggregate matching <paramref name="spec"/>, tracked, for a change to all of them.</summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, tracked.</returns>
    /// <remarks>
    /// Every returned aggregate stays in the change tracker until the scope ends. For a set-based change that
    /// needs no domain logic, use the bulk repository instead.
    /// </remarks>
    new Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Stages a new aggregate for insertion.</summary>
    /// <param name="aggregate">The aggregate to insert. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; nothing is written until the unit of work saves.</returns>
    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>Stages new aggregates for insertion.</summary>
    /// <param name="aggregates">The aggregates to insert. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; nothing is written until the unit of work saves.</returns>
    Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default);

    /// <summary>Stages an aggregate for update.</summary>
    /// <param name="aggregate">The aggregate. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; nothing is written until the unit of work saves.</returns>
    /// <remarks>
    /// <para>
    /// <b>Tracked aggregate</b> (loaded through this repository): nothing to do; only the changed columns are
    /// written. Calling it is harmless.
    /// </para>
    /// <para>
    /// <b>Detached aggregate</b> (deserialized, or loaded in another scope): it is <em>attached as modified</em>,
    /// so <strong>every</strong> column is written, and its child entities are attached too (new ones, with an
    /// unset key, as added). The creation audit columns are never overwritten. Prefer loading and changing a
    /// tracked aggregate; attach only when the whole aggregate state is authoritative.
    /// </para>
    /// </remarks>
    Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages an aggregate for update only if its stored version is still <paramref name="expectedVersion"/>
    /// (optimistic concurrency for HTTP <c>If-Match</c> / ETag).
    /// </summary>
    /// <param name="aggregate">The aggregate, tracked or detached (see <see cref="UpdateAsync(TAggregate, CancellationToken)"/>).</param>
    /// <param name="expectedVersion">
    /// The version the client last read: the aggregate's PostgreSQL <c>xmin</c> row version, as sent in the
    /// ETag.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; the version is checked when the unit of work saves.</returns>
    /// <exception cref="InvalidOperationException">The aggregate's model has no row-version concurrency token.</exception>
    /// <remarks>
    /// The save issues <c>UPDATE … WHERE id = @id AND xmin = @expectedVersion</c>; when another writer changed the
    /// row in between, no row matches and the save fails with a conflict (HTTP 409/412) instead of overwriting
    /// that change.
    /// </remarks>
    Task UpdateAsync(TAggregate aggregate, uint expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>Stages aggregates for update; see <see cref="UpdateAsync(TAggregate, CancellationToken)"/>.</summary>
    /// <param name="aggregates">The aggregates. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; nothing is written until the unit of work saves.</returns>
    Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages an aggregate for deletion. A soft-deletable aggregate is marked deleted and kept; any other is
    /// removed.
    /// </summary>
    /// <param name="aggregate">The aggregate. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; nothing is written until the unit of work saves.</returns>
    /// <remarks>
    /// A soft delete performed here raises no domain event. When other parts of the system must react, delete
    /// through the aggregate's own domain method instead.
    /// </remarks>
    Task DeleteAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>Stages aggregates for deletion; see <see cref="DeleteAsync"/>.</summary>
    /// <param name="aggregates">The aggregates. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task; nothing is written until the unit of work saves.</returns>
    Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default);
}
