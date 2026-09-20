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
/// <strong>Breaking change:</strong> <c>GetByIdAsync</c> has been removed from this interface.
/// Replace <c>readRepo.GetByIdAsync(id, cancellationToken)</c> with
/// <c>readRepo.GetBySpecAsync(new ByIdSpecification&lt;TAggregate, TId&gt;(id), cancellationToken)</c>.
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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first matching aggregate, or <see langword="null"/>.</returns>
    Task<TAggregate?> GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all aggregates that satisfy the specification.
    /// Apply <see cref="ISpecification{T}.Skip"/> and <see cref="ISpecification{T}.Take"/> on the
    /// specification for paged results.
    /// </summary>
    /// <param name="spec">The specification describing the desired aggregates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of matching aggregates (empty, never <see langword="null"/>).</returns>
    Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of aggregates that satisfy the specification, ignoring any
    /// <see cref="ISpecification{T}.Skip"/>/<see cref="ISpecification{T}.Take"/> paging the
    /// specification declares — a count is always the count of every matching row, not a page of it.
    /// </summary>
    /// <param name="spec">The specification to count against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The number of matching aggregates, as a <see cref="long"/> — consistent with
    /// <see cref="SharedKernel.Contracts.Pagination.PagedList{T}"/>'s own <see cref="long"/> total
    /// (an <see cref="int"/> count could silently overflow on a large table).
    /// </returns>
    Task<long> CountAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when at least one aggregate satisfies the specification.
    /// </summary>
    /// <param name="spec">The specification to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if any aggregate matches; otherwise <see langword="false"/>.</returns>
    Task<bool> AnyAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all aggregates whose identity is in the provided collection.
    /// </summary>
    /// <param name="ids">The identities to look up. May be empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A read-only list containing only those aggregates whose ID was found in the store.
    /// Result order is not guaranteed. Missing IDs produce no entry. An empty input produces an empty result.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>CORRECTED:</strong> against PostgreSQL, this translates to a SINGLE
    /// array-typed parameter (<c>WHERE "Id" = ANY(@ids)</c>) — not a SQL-Server-style per-value
    /// <c>IN (v1, v2, v3,...)</c> expansion. The prior "performance degrades above 1000 IDs"
    /// guidance was written assuming that SQL-Server shape (which does have a real ~2100-parameter
    /// ceiling); Npgsql's <c>= ANY(@array)</c> translation has no such per-value parameter-count
    /// limit. The real practical constraint is the serialized array parameter's payload size and the
    /// materialized result set's memory footprint, not parameter count. Result order is not
    /// guaranteed; missing IDs produce no entry; an empty input produces an empty result.
    /// </para>
    /// <para>
    /// See <see cref="GetByIdsChunkedAsync"/> for an opt-in sibling that issues bounded, sequential
    /// round trips instead of one — useful when a caller deliberately wants bounded per-query
    /// memory/payload despite <c>= ANY(@array)</c> not strictly requiring it. A re-derived guardrail:
    /// consider <see cref="GetByIdsChunkedAsync"/> above roughly 50,000 IDs to bound peak memory;
    /// below that, this single-query method remains efficient.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<TAggregate>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all aggregates whose identity is in the provided collection, issued as
    /// <c>ceil(N / chunkSize)</c> sequential <see cref="GetByIdsAsync"/>-equivalent round trips
    /// instead of one.
    /// </summary>
    /// <param name="ids">The identities to look up. May be empty.</param>
    /// <param name="chunkSize">
    /// The maximum number of identities per round trip. Must be at least 1.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A read-only list containing only those aggregates whose ID was found in the store, across
    /// all chunks. Result order is not guaranteed. Missing IDs produce no entry.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A purely additive, OPT-IN sibling: <see cref="GetByIdsAsync"/>'s own
    /// single-query <c>= ANY(@array)</c> behavior is completely unchanged by this method's
    /// existence. Choose this method deliberately when bounded per-query memory/payload is wanted
    /// over the single round trip <see cref="GetByIdsAsync"/> issues — see that method's remarks for
    /// the corrected guidance on when chunking is actually warranted (roughly above 50,000 IDs).
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<TAggregate>> GetByIdsChunkedAsync(
        IEnumerable<TId> ids,
        int chunkSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged result containing aggregates that satisfy the specification together with
    /// total-count metadata.
    /// </summary>
    /// <param name="spec">
    /// The specification describing filter, ordering, and paging. The <c>Skip</c> and <c>Take</c>
    /// values on the spec drive the page window.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PagedList{TAggregate}"/> with the current page of items and pagination metadata.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Issues two database round-trips under the same <c>DbContext</c> scope: one count query
    /// (Skip/Take stripped) and one data query (full spec applied). Both share the same connection.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// </para>
    /// <para>
    /// The total is counted as a <see cref="long"/>. The page size is the specification's
    /// <c>Take</c>, so the page never holds more items than its size; a specification with no
    /// <c>Take</c> is reported as a single page holding every matching item.
    /// </para>
    /// </remarks>
    Task<PagedList<TAggregate>> ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects all aggregates that satisfy the specification to <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">
    /// The projection output type. Must be a reference type or a value type that EF Core can
    /// translate — typically a DTO or an anonymous type.
    /// </typeparam>
    /// <param name="spec">
    /// The projection specification, which supplies both the filtering/ordering/paging pipeline
    /// and the <c>Selector</c> expression applied after paging.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of projected results (empty, never <see langword="null"/>).</returns>
    /// <remarks>
    /// The <c>Selector</c> expression on <paramref name="spec"/> is applied after Skip/Take to
    /// preserve the paging-last invariant. The EF Core provider translates the selector into a
    /// SQL <c>SELECT</c> projection — only the columns referenced by the selector are fetched.
    /// </remarks>
    Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the first aggregate that satisfies the specification projected to
    /// <typeparamref name="TResult"/>, or <see langword="null"/> when none match.
    /// </summary>
    /// <typeparam name="TResult">
    /// The projection output type. Must be a reference type or a value type that EF Core can
    /// translate.
    /// </typeparam>
    /// <param name="spec">
    /// The projection specification supplying filter criteria and the <c>Selector</c> expression.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The first projected result, or <see langword="null"/> when no aggregate satisfies the
    /// specification.
    /// </returns>
    /// <remarks>
    /// The <c>Selector</c> expression is applied after the full aggregate pipeline (criteria,
    /// includes, ordering, paging) — consistent with the paging-last invariant.
    /// </remarks>
    Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged result containing projected <typeparamref name="TResult"/> instances that
    /// satisfy the specification, together with total-count metadata.
    /// </summary>
    /// <typeparam name="TResult">
    /// The projection output type. Use this method instead of <see cref="ListPagedAsync"/> when
    /// the caller needs DTOs rather than aggregate roots.
    /// </typeparam>
    /// <param name="spec">
    /// The projection specification supplying filter criteria, ordering, paging, and the
    /// <c>Selector</c> expression applied after paging.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PagedList{TResult}"/> with the current page of projected items and
    /// pagination metadata.
    /// </returns>
    /// <remarks>
    /// Issues two database round-trips under the same <c>DbContext</c> scope:
    /// <list type="number">
    /// <item><description>
    /// <strong>Count query:</strong> the specification is evaluated without projection and
    /// without Skip/Take via <c>GetQuery</c>, then <c>LongCountAsync</c> is called.
    /// </description></item>
    /// <item><description>
    /// <strong>Data query:</strong> the full specification (including projection and Skip/Take)
    /// is evaluated via <c>GetProjectedQuery</c>, then <c>ToListAsync</c> is called.
    /// </description></item>
    /// </list>
    /// Both queries share the same connection and <c>DbContext</c> scope.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// Page metadata follows the same rules as <see cref="ListPagedAsync"/>: a <see cref="long"/>
    /// total and a page size taken from the specification's <c>Take</c>.
    /// </remarks>
    Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams all aggregates that satisfy the specification as an asynchronous sequence,
    /// using constant memory regardless of result-set size.
    /// </summary>
    /// <param name="spec">The specification describing the desired aggregates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous sequence of matching aggregates.</returns>
    /// <remarks>
    /// Intended for large result sets (exports, batch processing) where materializing an
    /// <see cref="IReadOnlyList{T}"/> would be memory-prohibitive. <see cref="ISpecification{T}.Skip"/>
    /// and <see cref="ISpecification{T}.Take"/> are honored as a row-window applied before streaming
    /// begins.
    /// <para>
    /// <strong>Deviation:</strong> the EF Core implementation forces no-tracking behaviour
    /// unconditionally, regardless of <see cref="ISpecification{T}.AsNoTracking"/> — a long-lived
    /// streaming enumeration under change tracking would grow the change tracker unbounded for the
    /// lifetime of the enumeration.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<TAggregate> StreamAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams all aggregates that satisfy the specification, projected to
    /// <typeparamref name="TResult"/>, as an asynchronous sequence, using constant memory
    /// regardless of result-set size.
    /// </summary>
    /// <typeparam name="TResult">The projection output type.</typeparam>
    /// <param name="spec">
    /// The projection specification supplying filter criteria, ordering, paging, and the
    /// <c>Selector</c> expression applied after paging.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An asynchronous sequence of projected results.</returns>
    /// <remarks>
    /// Intended for large result sets (exports, batch processing) where materializing an
    /// <see cref="IReadOnlyList{T}"/> would be memory-prohibitive. <see cref="ISpecification{T}.Skip"/>
    /// and <see cref="ISpecification{T}.Take"/> are honored as a row-window applied before streaming
    /// begins.
    /// <para>
    /// <strong>Deviation:</strong> the EF Core implementation forces no-tracking behaviour
    /// unconditionally, regardless of <see cref="ISpecification{T}.AsNoTracking"/>.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single cursor/seek-paginated page of aggregates satisfying <paramref name="spec"/>.
    /// </summary>
    /// <typeparam name="TKey">The comparable sort-key type used for cursor/seek pagination.</typeparam>
    /// <param name="spec">
    /// The keyset specification supplying filter criteria, ordering, cursor position, and page size.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="SharedKernel.Contracts.Pagination.CursorPagedList{TAggregate}"/> for the
    /// requested page — <c>04.Contracts</c>'s wire type for a cursor-paginated result, with an opaque
    /// <c>NextCursor</c> built from the page's sort key and id via
    /// <see cref="SharedKernel.Contracts.Pagination.PageCursor.Encode{TKey, TId}"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The deep-pagination sibling of
    /// <see cref="ListPagedAsync"/>, for large or actively-written result sets where offset
    /// pagination's <c>O(n)</c> scan-and-discard cost is a real, measured problem, or for
    /// infinite-scroll/"load more" UI patterns.
    /// </para>
    /// <para>
    /// <strong>Hard constraint:</strong> passing a <c>KeysetSpecification&lt;TAggregate,TKey&gt;</c>
    /// to <see cref="ListAsync"/>, <see cref="GetBySpecAsync"/>, <see cref="CountAsync"/>, or
    /// <see cref="AnyAsync"/> instead compiles and runs, but silently ignores
    /// <c>AfterKey</c>/<c>AfterId</c> and always returns the first page — this method (and
    /// <see cref="ListKeysetProjectedAsync{TKey, TResult}"/>) are the ONLY entry points that honor
    /// the cursor.
    /// </para>
    /// </remarks>
    Task<SharedKernel.Contracts.Pagination.CursorPagedList<TAggregate>> ListKeysetAsync<TKey>(
        SharedKernel.Domain.Specifications.KeysetSpecification<TAggregate, TKey> spec,
        CancellationToken cancellationToken = default)
        where TKey : struct, IComparable<TKey>;

    /// <summary>
    /// Returns a single cursor/seek-paginated page of <paramref name="spec"/>'s aggregates,
    /// projected to <typeparamref name="TResult"/> via <paramref name="selector"/>.
    /// </summary>
    /// <typeparam name="TKey">The comparable sort-key type used for cursor/seek pagination.</typeparam>
    /// <typeparam name="TResult">The projection output type.</typeparam>
    /// <param name="spec">
    /// The keyset specification supplying filter criteria, ordering, cursor position, and page size.
    /// </param>
    /// <param name="selector">
    /// The projection expression, translated by the EF Core provider into a SQL <c>SELECT</c>
    /// projection — only the columns the selector references are fetched. Applied after paging,
    /// consistent with every other projected read method on this interface.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="SharedKernel.Contracts.Pagination.CursorPagedList{TResult}"/> for the requested
    /// page.
    /// </returns>
    /// <remarks>
    /// Additive sibling of <see cref="ListKeysetAsync{TKey}"/> for callers that need DTOs
    /// rather than tracked/no-tracking aggregate roots — the projected counterpart to
    /// <see cref="ListPagedProjectedAsync{TResult}"/>.
    /// </remarks>
    Task<SharedKernel.Contracts.Pagination.CursorPagedList<TResult>> ListKeysetProjectedAsync<TKey, TResult>(
        SharedKernel.Domain.Specifications.KeysetSpecification<TAggregate, TKey> spec,
        System.Linq.Expressions.Expression<Func<TAggregate, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TKey : struct, IComparable<TKey>;
}
