using System.Runtime.CompilerServices;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// The neutral, per-record-type contract for upserting, deleting, querying, and walking one vector
/// collection — the only type application code should inject for read/write access to one collection.
/// </summary>
/// <typeparam name="TRecord">The vector record type.</typeparam>
/// <remarks>
/// <para>
/// <b>No <c>WriteConsistency</c>/consistency-level parameter on any write — the single most important
/// "one more knob" this design rejected:</b> Qdrant's write model (a <c>wait</c> boolean — block until
/// searchable, or return immediately) and Milvus's write model (insert returns immediately; visibility
/// is governed by the consistency level of a <em>subsequent query</em>, not a property of the write
/// call at all) are not the same knob wearing different clothes. Every write returns immediately with a
/// receipt, and <see cref="WaitUntilQueryableAsync"/> is a separate, explicit, opt-in deferred barrier —
/// Qdrant polls the point via its own client until visible; Milvus re-issues a bounded read using the
/// receipt's opaque <c>ProviderToken</c> as a <c>guarantee_timestamp</c> until it succeeds or the
/// timeout elapses.
/// </para>
/// <para>
/// <b>Upsert only:</b> <see cref="UpsertAsync"/> is keyed on <see cref="IVectorRecord.Id"/> — no
/// Create-vs-Update split, mirroring <c>09.Search</c>'s <c>IndexAsync</c> reasoning (an upstream sync
/// pipeline's at-least-once delivery makes the distinction meaningless on both engines).
/// </para>
/// <para>
/// <b>No optimistic concurrency:</b> neither Qdrant nor Milvus offers a comparable primitive to EF
/// Core's rowversion or ElasticSearch's <c>if_seq_no</c>. A sync pipeline feeding these methods must
/// guarantee ordering upstream by partitioning the change stream on <see cref="IVectorRecord.Id"/> —
/// documented, not enforced.
/// </para>
/// <para>
/// <b>Bulk partial failure is not collapsed:</b> <see cref="UpsertManyAsync"/>/<see cref="DeleteManyAsync"/>
/// return <see cref="Result{T}"/>, and the <see cref="Result{T}"/> is success even when
/// <see cref="VectorBulkReceipt.Failures"/> is non-empty.
/// </para>
/// <para>
/// <b><see cref="DeleteByFilterAsync"/> takes <see cref="TenantScope"/> as a mandatory separate
/// parameter:</b> never part of the <see cref="VectorFilter"/> tree — a dropped tenant clause on a bulk
/// delete is cross-tenant data destruction.
/// </para>
/// <para>
/// <b><see cref="WaitUntilQueryableAsync"/> must not be used for read-your-writes on a request path:</b>
/// it is the deferred barrier for background/bulk-import callers wanting one barrier after N writes
/// instead of N barriers. Returns <c>IntelligenceErrors.WriteTimeout</c> on expiry — the write may still
/// land; the error says so explicitly so callers do not retry blindly assuming it did not.
/// </para>
/// <para>
/// <b>Model-identity/dimension/metric validation is the first thing every write does:</b> before any
/// I/O, the adapter checks <c>record.ModelId == definition.EmbeddingModelId</c> and
/// <c>record.Vector.Length == definition.Dimension</c>, returning
/// <c>IntelligenceErrors.EmbeddingModelMismatch</c>/<c>.DimensionMismatch</c> on failure.
/// <see cref="VectorDistanceMetric"/> has no per-record representation to validate — it is a property
/// of the collection alone.
/// </para>
/// <para>
/// <b><see cref="GetAsync"/> is tenant-checked:</b> implemented as a filtered point-lookup, not a raw
/// get, whenever the collection definition declares a <c>TenantField</c> — a caller must not read
/// another tenant's record by guessing an id. A miss returns <c>IntelligenceErrors.RecordNotFound</c> —
/// never <see langword="null"/>, never a thrown exception.
/// </para>
/// <para>
/// <b><see cref="CountAsync"/> is exact on both engines:</b> Qdrant's count endpoint and Milvus's query
/// with <c>COUNT(*)</c> both return exact counts — unlike full-text search, there is no engine-side
/// estimate to reconcile.
/// </para>
/// <para>
/// <b><see cref="ScrollAsync"/> is not <see cref="Result{T}"/>-wrapped</b> — the established
/// <c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c> streaming precedent. A transport failure
/// mid-stream surfaces as <c>IntelligenceStreamException</c> from <c>MoveNextAsync</c>. It is a record
/// walk (Qdrant scroll API / Milvus query iterator), not a ranked similarity search — it takes a
/// <see cref="VectorFilter"/> but no vector, and exists for reindex/export/re-embed-source enumeration.
/// Ordering is unspecified and must not be relied upon.
/// </para>
/// </remarks>
public interface IVectorCollection<TRecord>
    where TRecord : class, IVectorRecord
{
    /// <summary>Gets the name of the collection this instance targets.</summary>
    string CollectionName { get; }

    // --- write ---

    /// <summary>Upserts a single record.</summary>
    Task<Result<VectorWriteReceipt>> UpsertAsync(
        TRecord record,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>Upserts many records in one logical batch operation.</summary>
    Task<Result<VectorBulkReceipt>> UpsertManyAsync(
        IReadOnlyCollection<TRecord> records,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a single record by id.</summary>
    Task<Result<VectorWriteReceipt>> DeleteAsync(
        string id,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes many records by id in one logical batch operation.</summary>
    Task<Result<VectorBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> ids,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every record matching <paramref name="filter"/>. <paramref name="tenantScope"/> is a
    /// mandatory separate parameter, never part of <paramref name="filter"/> — a dropped tenant clause
    /// on a delete is cross-tenant data destruction.
    /// </summary>
    Task<Result<VectorWriteReceipt>> DeleteByFilterAsync(
        VectorFilter filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocks until the write identified by <paramref name="receipt"/> is guaranteed queryable, or
    /// <paramref name="timeout"/> elapses. The deferred barrier for background/bulk-import callers —
    /// must not be used to implement read-your-writes on a request path.
    /// </summary>
    Task<Result> WaitUntilQueryableAsync(
        VectorWriteReceipt receipt,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    // --- read ---

    /// <summary>Executes a similarity query.</summary>
    Task<Result<VectorQueryResults<TRecord>>> QueryAsync(
        VectorQuery query,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single record by id. Returns a not-found error — never <see langword="null"/>,
    /// never a thrown exception — when the record does not exist or belongs to a different tenant.
    /// </summary>
    Task<Result<TRecord>> GetAsync(
        string id,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the exact count of records matching <paramref name="filter"/> (or the whole collection
    /// when <paramref name="filter"/> is <see langword="null"/>), scoped to <paramref name="tenantScope"/>.
    /// </summary>
    Task<Result<long>> CountAsync(
        VectorFilter? filter,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);

    // --- corpus walk ---

    /// <summary>
    /// Walks every record matching <paramref name="filter"/> (or the whole collection when
    /// <paramref name="filter"/> is <see langword="null"/>), scoped to <paramref name="tenantScope"/>,
    /// in batches of <paramref name="batchSize"/>. For reindex/export/re-embed-source enumeration — not
    /// a ranked similarity search. Ordering is unspecified.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token used to stop paging mid-enumeration. Implementations apply
    /// <see cref="EnumeratorCancellationAttribute"/> to this parameter on their concrete
    /// async-iterator method — the attribute has no effect on an interface declaration, so it is
    /// intentionally omitted here.
    /// </param>
    IAsyncEnumerable<TRecord> ScrollAsync(
        VectorFilter? filter,
        TenantScope tenantScope,
        int batchSize,
        CancellationToken cancellationToken = default);
}
