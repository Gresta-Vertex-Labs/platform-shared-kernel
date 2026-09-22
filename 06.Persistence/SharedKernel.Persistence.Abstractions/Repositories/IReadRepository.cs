using System.Linq.Expressions;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Read-side repository for an aggregate root: every query runs <strong>without change tracking</strong> and
/// is described by an <see cref="ISpecification{T}"/>; no <see cref="IQueryable{T}"/> is exposed.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>Registration.</b> Registered automatically for every aggregate root the service's DbContexts map; inject
/// <c>IReadRepository&lt;Order, OrderId&gt;</c> without writing a class. Subclass the EF Core implementation only
/// to add custom queries.
/// </para>
/// <para>
/// <b>No tracking.</b> Returned aggregates are detached snapshots: changing them saves nothing. Load an aggregate
/// you intend to change through <see cref="IRepository{TAggregate, TId}"/>.
/// </para>
/// <para>
/// <b>Paging at the call site.</b> Offset pages come from <see cref="ListPagedAsync"/> with a
/// <see cref="PageRequest"/>; keyset (cursor) pages from <see cref="ListKeysetAsync{TKey}"/> with a
/// <see cref="CursorPageRequest"/>. Both reject a specification that declares its own Skip/Take.
/// </para>
/// <para>
/// <b>Soft delete and tenancy.</b> Soft-deleted aggregates are hidden unless the specification sets
/// <see cref="ISpecification{T}.IncludeDeleted"/>; tenant isolation always applies.
/// </para>
/// </remarks>
public interface IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Returns the aggregate with the given identity, or <see langword="null"/> when there is none.</summary>
    /// <param name="id">The aggregate's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete aggregate (see remarks), untracked; or <see langword="null"/>.</returns>
    /// <remarks>
    /// Loads the whole aggregate: the navigations its model marks with <c>Navigation(...).AutoInclude()</c> are
    /// loaded, plus whatever an implementation's aggregate-query hook adds. Soft-deleted aggregates are not
    /// returned.
    /// </remarks>
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Returns the aggregates whose identity is in <paramref name="ids"/>, in no particular order.</summary>
    /// <param name="ids">The identities to look up; may be empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregates found, untracked; missing identities produce no entry.</returns>
    /// <remarks>
    /// Runs one query with a single array parameter (<c>WHERE id = ANY(@ids)</c>), so there is no per-value
    /// parameter limit; the practical limit is the size of the result.
    /// </remarks>
    Task<IReadOnlyList<TAggregate>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default);

    /// <summary>Returns whether an aggregate with the given identity exists, without loading it.</summary>
    /// <param name="id">The aggregate's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when a (not soft-deleted) aggregate exists.</returns>
    Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Returns the first aggregate matching <paramref name="spec"/>, or <see langword="null"/>.</summary>
    /// <param name="spec">The query; its ordering decides which match is first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first match, untracked; or <see langword="null"/>.</returns>
    Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Returns every aggregate matching <paramref name="spec"/>.</summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, untracked; empty, never <see langword="null"/>.</returns>
    /// <remarks>For large or client-driven results use a paged method instead.</remarks>
    Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Counts every aggregate matching <paramref name="spec"/>, ignoring any Skip/Take it declares.</summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matching aggregates.</returns>
    Task<long> CountAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Returns whether at least one aggregate matches <paramref name="spec"/>.</summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when any aggregate matches.</returns>
    Task<bool> AnyAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Returns one offset page of the aggregates matching <paramref name="spec"/>, with the total count.</summary>
    /// <param name="spec">
    /// The query. Must declare a primary sort (ending in a unique key such as the identity, so pages neither
    /// overlap nor skip rows) and no Skip/Take.
    /// </param>
    /// <param name="page">The validated page number and size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, untracked, with the total number of matching aggregates.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="spec"/> has no primary sort or declares Skip/Take.
    /// </exception>
    /// <remarks>Runs two queries: a count and the page.</remarks>
    Task<PagedList<TAggregate>> ListPagedAsync(
        ISpecification<TAggregate> spec,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one keyset (cursor) page of the aggregates matching <paramref name="spec"/>, ordered by
    /// <paramref name="keySelector"/> and then by identity.
    /// </summary>
    /// <typeparam name="TKey">The sort key's type, such as <see cref="DateTimeOffset"/>.</typeparam>
    /// <param name="spec">The filter. Must declare no ordering and no Skip/Take; the key and identity order the page.</param>
    /// <param name="page">
    /// The validated cursor request: no cursor for the first page, otherwise the
    /// <see cref="CursorPagedList{T}.NextCursor"/> of the previous page.
    /// </param>
    /// <param name="keySelector">
    /// The sort key, a plain member path such as <c>o =&gt; o.CreatedOn</c> on a non-nullable column. The
    /// identity is the tiebreak, so the order is total.
    /// </param>
    /// <param name="descending"><see langword="true"/> to sort newest/largest first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The page, untracked, with <see cref="CursorPagedList{T}.NextCursor"/> set when more rows follow.
    /// </returns>
    /// <exception cref="InvalidOperationException"><paramref name="spec"/> declares ordering or Skip/Take.</exception>
    /// <exception cref="SharedKernel.Core.Exceptions.ValidationException">
    /// The cursor is malformed or was issued for a different key type (<see cref="PaginationErrorCodes.CursorInvalid"/>).
    /// </exception>
    /// <remarks>
    /// Runs one seek query (<c>WHERE (key, id) &gt; (@key, @id)</c>) that reads one row beyond the page to know
    /// whether another page exists. Constant cost at any depth, stable while rows are inserted. A cursor only
    /// fits the key and direction that issued it; the cursor is not signed, so the query's tenant and
    /// authorization filters always still apply.
    /// </remarks>
    Task<CursorPagedList<TAggregate>> ListKeysetAsync<TKey>(
        ISpecification<TAggregate> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : notnull;

    /// <summary>
    /// Streams every aggregate matching <paramref name="spec"/> without buffering the result.
    /// </summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, untracked, as they are read.</returns>
    /// <remarks>For exports and batch jobs. The connection stays open until enumeration ends.</remarks>
    IAsyncEnumerable<TAggregate> StreamAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>Returns the first projected result of <paramref name="spec"/>, or its default.</summary>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="spec">The projection query; the selector is translated to SQL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first projected result, or <see langword="default"/>.</returns>
    Task<TResult?> FirstOrDefaultProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);

    /// <summary>Returns every projected result of <paramref name="spec"/>.</summary>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="spec">The projection query; the selector is translated to SQL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected results; empty, never <see langword="null"/>.</returns>
    Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one offset page of projected results, with the total count.</summary>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="spec">The projection query. Must declare a primary sort and no Skip/Take.</param>
    /// <param name="page">The validated page number and size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of projected results with the total number of matches.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="spec"/> has no primary sort or declares Skip/Take.
    /// </exception>
    Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one keyset (cursor) page of projected results.</summary>
    /// <typeparam name="TKey">The sort key's type.</typeparam>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="spec">The projection query. Must declare no ordering and no Skip/Take.</param>
    /// <param name="page">The validated cursor request.</param>
    /// <param name="keySelector">The sort key on the aggregate (not on the projection), a plain member path.</param>
    /// <param name="descending"><see langword="true"/> to sort newest/largest first.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of projected results.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="spec"/> declares ordering or Skip/Take.</exception>
    /// <exception cref="SharedKernel.Core.Exceptions.ValidationException">The cursor is malformed.</exception>
    /// <remarks>
    /// The projection, the sort key and the identity are all read in the same SQL <c>SELECT</c>; the cursor is
    /// built from the key and identity, so the projection does not need to contain them.
    /// </remarks>
    Task<CursorPagedList<TResult>> ListKeysetProjectedAsync<TKey, TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : notnull;

    /// <summary>Streams every projected result of <paramref name="spec"/> without buffering the result.</summary>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="spec">The projection query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projected results as they are read.</returns>
    IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);
}
