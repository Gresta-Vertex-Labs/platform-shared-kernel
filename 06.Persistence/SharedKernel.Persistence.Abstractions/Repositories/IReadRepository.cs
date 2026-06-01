using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Read-side repository contract for a DDD aggregate root.
/// All queries are specification-driven — no raw <see cref="System.Linq.IQueryable{T}"/> is exposed.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">
/// The aggregate's identity type. Must be non-null.
/// </typeparam>
/// <remarks>
/// <para>
/// Use <c>ReadOnlySpecification&lt;T&gt;</c> subclasses (which set <see cref="ISpecification{T}.AsNoTracking"/>
/// to <see langword="true"/>) on read-heavy paths to avoid unnecessary change-tracking overhead.
/// Use <c>PagedSpecification&lt;T&gt;</c> for paged list queries.
/// </para>
/// <para>
/// This interface is intentionally read-only — mutations are performed via <see cref="IRepository{TAggregate,TId}"/>.
/// </para>
/// </remarks>
public interface IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Retrieves an aggregate by its unique identity, or <see langword="null"/> when not found.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aggregate root, or <see langword="null"/> if no match exists.</returns>
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default);

    /// <summary>
    /// Returns the first aggregate that satisfies the specification, or <see langword="null"/> when none match.
    /// </summary>
    /// <param name="spec">The specification describing the desired aggregate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first matching aggregate, or <see langword="null"/>.</returns>
    Task<TAggregate?> GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct = default);

    /// <summary>
    /// Returns all aggregates that satisfy the specification.
    /// Apply <see cref="ISpecification{T}.Skip"/> and <see cref="ISpecification{T}.Take"/> on the
    /// specification for paged results.
    /// </summary>
    /// <param name="spec">The specification describing the desired aggregates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of matching aggregates (empty, never <see langword="null"/>).</returns>
    Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of aggregates that satisfy the specification.
    /// </summary>
    /// <param name="spec">The specification to count against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of matching aggregates.</returns>
    Task<int> CountAsync(ISpecification<TAggregate> spec, CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> when at least one aggregate satisfies the specification.
    /// </summary>
    /// <param name="spec">The specification to test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if any aggregate matches; otherwise <see langword="false"/>.</returns>
    Task<bool> AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct = default);
}
