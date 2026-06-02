using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;

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
/// <para>
/// <strong>Breaking change (P-080):</strong> <c>GetByIdAsync</c> has been removed from this interface.
/// Replace <c>readRepo.GetByIdAsync(id, ct)</c> with
/// <c>readRepo.GetBySpecAsync(new ByIdSpecification&lt;TAggregate, TId&gt;(id), ct)</c>.
/// <see cref="IRepository{TAggregate,TId}"/> (write side) retains its own <c>GetByIdAsync</c>.
/// </para>
/// </remarks>
public interface IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
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

    /// <summary>
    /// Returns all aggregates whose identity is in the provided collection.
    /// </summary>
    /// <param name="ids">The identities to look up. May be empty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only list containing only those aggregates whose ID was found in the store.
    /// Result order is not guaranteed. Missing IDs produce no entry. An empty input produces an empty result.
    /// </returns>
    /// <remarks>
    /// Translates to an SQL <c>IN (...)</c> clause. Performance degrades above 1000 IDs —
    /// chunk at the application layer for large collections.
    /// </remarks>
    Task<IReadOnlyList<TAggregate>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct = default);

    /// <summary>
    /// Returns a paged result containing aggregates that satisfy the specification together with
    /// total-count metadata.
    /// </summary>
    /// <param name="spec">
    /// The specification describing filter, ordering, and paging. The <c>Skip</c> and <c>Take</c>
    /// values on the spec drive the page window.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PagedList{TAggregate}"/> with the current page of items and pagination metadata.
    /// </returns>
    /// <remarks>
    /// Issues two database round-trips under the same <c>DbContext</c> scope: one count query
    /// (Skip/Take stripped) and one data query (full spec applied). Both share the same connection.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// </remarks>
    Task<PagedList<TAggregate>> ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct = default);

    /// <summary>
    /// Projects all aggregates that satisfy the specification to <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">The projection output type.</typeparam>
    /// <param name="spec">
    /// The projection specification, which supplies both the filtering/ordering/paging pipeline
    /// and the selector expression applied after paging.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of projected results (empty, never <see langword="null"/>).</returns>
    /// <remarks>
    /// The selector expression is applied after Skip/Take to preserve the paging-last invariant.
    /// </remarks>
    Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the first aggregate that satisfies the specification projected to
    /// <typeparamref name="TResult"/>, or <see langword="null"/> when none match.
    /// </summary>
    /// <typeparam name="TResult">The projection output type.</typeparam>
    /// <param name="spec">The projection specification.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first projected result, or <see langword="null"/>.</returns>
    Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default);
}
