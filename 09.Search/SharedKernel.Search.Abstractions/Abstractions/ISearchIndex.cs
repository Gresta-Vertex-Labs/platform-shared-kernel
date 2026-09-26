using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Abstractions;

/// <summary>
/// The neutral, per-document-type contract for indexing, deleting, searching, and walking a search
/// index — the only type application code should inject for read/write access to one index.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <para>
/// Every write takes a mandatory, non-defaulted <see cref="SearchWriteConsistency"/> — a bare
/// fire-and-forget write is dishonest on both engines (see <see cref="SearchWriteConsistency"/>'s own
/// remarks). Every read and every filtered write takes a mandatory, non-defaulted
/// <see cref="TenantScope"/> — see <see cref="TenantScope"/>'s own remarks for why it is a separate
/// parameter rather than a member of <see cref="SearchRequest"/>.
/// </para>
/// <para>
/// <b>Upsert only:</b> <see cref="IndexAsync"/> is an upsert keyed on
/// <see cref="ISearchDocument.DocumentId"/>. There is no create-vs-update split, because Meilisearch
/// has no create-if-absent primitive and at-least-once delivery from any upstream sync pipeline makes
/// the distinction meaningless.
/// </para>
/// <para>
/// <b>No optimistic concurrency, and no fake:</b> ElasticSearch has external versioning and
/// <c>if_seq_no</c>/<c>if_primary_term</c>; Meilisearch has nothing comparable and is
/// last-write-by-arrival-order. Rather than carry a version member one adapter would silently ignore,
/// this contract carries none: a sync pipeline feeding these methods must guarantee ordering upstream
/// by partitioning the change stream on <see cref="ISearchDocument.DocumentId"/>. Documented, not
/// enforced, and the domain's sharpest sharp edge.
/// </para>
/// <para>
/// <b>Bulk partial failure is not collapsed:</b> <c>IndexManyAsync</c>/<c>DeleteManyAsync</c>
/// return a successful <see cref="Result{T}"/> carrying a
/// <see cref="SearchBulkReceipt"/> even when <see cref="SearchBulkReceipt.Failures"/> is non-empty.
/// <c>Result.Failure</c> is reserved for "the request itself did not execute".
/// </para>
/// <para>
/// <b>Fail loud, never degrade:</b> every request is validated against the registered
/// <see cref="SearchIndexDefinition"/> before any I/O. The adapter must never drop a clause, coerce it
/// to a text match, or post-filter in memory.
/// </para>
/// <para>
/// <b><see cref="GetAsync"/> is tenant-checked:</b> the adapter implements it as a filtered
/// single-hit search, not a raw get, whenever the index definition declares a
/// <see cref="SearchIndexDefinition.TenantField"/> — a get-by-id without a tenant predicate would let
/// a caller read any tenant's document by guessing an id.
/// </para>
/// <para>
/// <b><see cref="EnumerateAsync"/> is not <c>Result</c>-wrapped</b> — the one documented exception to
/// the Result-first rule in this domain, following the established <c>06.Persistence</c>/
/// <c>08.Storage</c> streaming-read precedent. A transport failure mid-stream surfaces as
/// <c>SearchStreamException</c> from <c>MoveNextAsync</c>. It is a document walk over the engine's
/// document-listing endpoint, not a deep search — so it is not subject to a provider's total-hits
/// pagination ceiling — and its ordering is unspecified until verified against a real container.
/// </para>
/// </remarks>
public interface ISearchIndex<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>Gets the name of the index this instance targets.</summary>
    string IndexName { get; }

    /// <summary>Upserts a single document.</summary>
    Task<Result<SearchWriteReceipt>> IndexAsync(
        TDocument document,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts many documents in one logical batch operation. Delegates to the
    /// <see cref="IndexManyAsync(IReadOnlyCollection{TDocument}, SearchWriteConsistency, SearchBulkWriteOptions, CancellationToken)"/>
    /// overload passing <see cref="SearchBulkWriteOptions.Default"/> — today's unthrottled, sequential
    /// behavior, unchanged.
    /// </summary>
    Task<Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts many documents in one logical batch operation, honoring the opt-in backpressure
    /// controls in <paramref name="bulkOptions"/> over the batch-dispatch loop the provider already
    /// runs internally.
    /// </summary>
    Task<Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a single document by id.</summary>
    Task<Result<SearchWriteReceipt>> DeleteAsync(
        string documentId,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes many documents by id in one logical batch operation. Delegates to the
    /// <see cref="DeleteManyAsync(IReadOnlyCollection{string}, SearchWriteConsistency, SearchBulkWriteOptions, CancellationToken)"/>
    /// overload passing <see cref="SearchBulkWriteOptions.Default"/> — today's unthrottled, sequential
    /// behavior, unchanged.
    /// </summary>
    Task<Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes many documents by id in one logical batch operation, honoring the opt-in backpressure
    /// controls in <paramref name="bulkOptions"/> over the batch-dispatch loop the provider already
    /// runs internally.
    /// </summary>
    Task<Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every document matching <paramref name="filter"/>. <paramref name="tenantScope"/> is a
    /// mandatory separate parameter, never part of <paramref name="filter"/> — a dropped tenant
    /// clause on a delete is cross-tenant data destruction.
    /// </summary>
    Task<Result<SearchWriteReceipt>> DeleteByFilterAsync(
        SearchFilter filter,
        TenantScope tenantScope,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every document in the index.</summary>
    Task<Result> ClearAsync(SearchWriteConsistency consistency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocks until the write identified by <paramref name="receipt"/> is guaranteed searchable, or
    /// <paramref name="timeout"/> elapses. The deferred barrier for callers who issued
    /// <see cref="SearchWriteConsistency.Accepted"/> writes — must not be used to implement
    /// read-your-writes on a request path.
    /// </summary>
    Task<Result> WaitUntilSearchableAsync(
        SearchWriteReceipt receipt,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a search request.</summary>
    Task<Result<SearchResults<TDocument>>> SearchAsync(
        SearchRequest request,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single document by id. Returns a not-found error — never
    /// <see langword="null"/>, never a thrown exception — when the document does not exist or belongs
    /// to a different tenant.
    /// </summary>
    Task<Result<TDocument>> GetAsync(
        string documentId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the documents matching <paramref name="filter"/> (or the whole index when
    /// <paramref name="filter"/> is <see langword="null"/>), scoped to <paramref name="tenantScope"/>.
    /// </summary>
    /// <remarks>
    /// <b>Check <see cref="SearchCount.Accuracy"/> before trusting the figure.</b> ElasticSearch
    /// answers from its <c>_count</c> API and is always
    /// <see cref="TotalHitsAccuracy.Exact"/>. Meilisearch has no count endpoint and must read
    /// <c>totalHits</c> off a paginated search, which the engine caps at the index's
    /// <see cref="SearchIndexDefinition.MaxTotalHits"/> — so a count that reaches that ceiling comes
    /// back as <see cref="TotalHitsAccuracy.LowerBound"/> rather than being published as fact. See
    /// <see cref="SearchCount"/>.
    /// </remarks>
    Task<Result<SearchCount>> CountAsync(
        SearchFilter? filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks every document matching <paramref name="filter"/> (or the whole index when
    /// <paramref name="filter"/> is <see langword="null"/>), scoped to <paramref name="tenantScope"/>,
    /// in batches of <paramref name="batchSize"/>. For reindex, export, and reconciliation — not a
    /// relevance-ordered deep search. Ordering is unspecified.
    /// </summary>
    IAsyncEnumerable<TDocument> EnumerateAsync(
        SearchFilter? filter,
        TenantScope tenantScope,
        int batchSize,
        CancellationToken cancellationToken = default);
}
