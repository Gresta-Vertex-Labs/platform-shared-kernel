using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Sparse;

/// <summary>
/// A Qdrant-exclusive dense+sparse hybrid (RRF-fused) similarity query — declared only in this
/// package, never <c>SharedKernel.AI.Abstractions</c>, since Milvus's sparse-vector fusion semantics
/// have not been verified by this domain as identical.
/// </summary>
/// <remarks>
/// <b>Scoped to querying only, deliberately:</b> sparse-vector ingestion is expected via
/// <see cref="Raw.IQdrantRawClientAccessor"/> until a future phase adds a dedicated write path — a
/// narrower, lower-risk surface than a full untested hybrid write contract.
/// </remarks>
public interface IQdrantHybridQueryAccessor<TRecord>
    where TRecord : class, IVectorRecord
{
    /// <summary>
    /// Runs a dense+sparse hybrid query, fusing <paramref name="denseQuery"/> against
    /// <paramref name="sparseQueryVector"/> (targeting the named sparse vector
    /// <paramref name="sparseVectorName"/>) via Reciprocal Rank Fusion.
    /// </summary>
    Task<Result<VectorQueryResults<TRecord>>> QueryHybridAsync(
        VectorQuery denseQuery,
        string sparseVectorName,
        IReadOnlyList<QdrantSparseVectorEntry> sparseQueryVector,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default);
}
