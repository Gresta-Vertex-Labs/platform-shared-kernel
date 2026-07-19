using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Cursors;

/// <summary>
/// The ElasticSearch-exclusive relevance-ordered deep-pagination contract — declared here, not in
/// <c>SharedKernel.Search.Abstractions</c>, so referencing it takes a compile-time dependency on this
/// package.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <para>
/// Distinct from <c>ISearchIndex&lt;TDocument&gt;.EnumerateAsync</c>, which is an unordered corpus
/// walk. Meilisearch has no <c>search_after</c> and no point-in-time at any price, and its
/// <c>limit</c>+<c>offset</c> is hard-capped by <c>maxTotalHits</c> — paging it in a loop to fake a
/// cursor would silently stop at the ceiling, so it is not faked here either.
/// </para>
/// <para>
/// Built on point-in-time plus <c>search_after</c>, never scroll — Elastic explicitly de-recommends
/// the scroll API for deep pagination.
/// </para>
/// </remarks>
public interface ICursorSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>
    /// Streams every hit matching <paramref name="request"/> in relevance order, past any provider
    /// pagination ceiling. Not <c>Result</c>-wrapped — the same streaming precedent as
    /// <c>ISearchIndex.EnumerateAsync</c>; a transport failure surfaces as <c>SearchStreamException</c>
    /// from <c>MoveNextAsync</c>. <paramref name="request"/>'s <c>PageSize</c> is reinterpreted as the
    /// per-round-trip batch size; <c>Page</c> greater than 1 is rejected.
    /// </summary>
    IAsyncEnumerable<SearchHit<TDocument>> StreamAsync(
        SearchRequest request,
        TenantScope tenantScope,
        TimeSpan keepAlive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a resumable cursor for <paramref name="request"/> — for a long-running export that
    /// checkpoints its cursor across process restarts, which <see cref="IAsyncEnumerable{T}"/> cannot
    /// express.
    /// </summary>
    Task<Result<SearchCursor>> OpenCursorAsync(
        SearchRequest request, TenantScope tenantScope, TimeSpan keepAlive, CancellationToken cancellationToken = default);

    /// <summary>Reads the next page from <paramref name="cursor"/>.</summary>
    Task<Result<CursorPage<TDocument>>> ReadCursorAsync(SearchCursor cursor, CancellationToken cancellationToken = default);

    /// <summary>Closes <paramref name="cursor"/>, releasing its underlying point-in-time.</summary>
    Task<Result> CloseCursorAsync(SearchCursor cursor, CancellationToken cancellationToken = default);
}
