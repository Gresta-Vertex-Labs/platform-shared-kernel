namespace SharedKernel.Persistence.PostgreSQL.Vector;

/// <summary>
/// The pgvector distance/similarity metric used by <see cref="VectorOrderingExpressions.ByDistance{TAggregate}"/>
/// to order a query by proximity to a query vector.
/// </summary>
/// <remarks>
/// WO-053/P-339 — a deliberate narrowing of the six distance/similarity members
/// <c>Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions</c> actually exposes
/// (<c>CosineDistance</c>/<c>L2Distance</c>/<c>L1Distance</c>/<c>HammingDistance</c>/
/// <c>JaccardDistance</c>/<c>MaxInnerProduct</c>, confirmed via direct reflection against the real
/// shipped <c>Pgvector.EntityFrameworkCore</c> 0.3.0 assembly) to the two most common similarity
/// metrics. A future phase may extend this enum (<c>L1</c>/<c>Hamming</c>/<c>Jaccard</c>/
/// <c>MaxInnerProduct</c>) if a concrete consumer need emerges — not attempted here.
/// </remarks>
public enum VectorDistanceMetric
{
    /// <summary>Cosine distance — <c>Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions.CosineDistance</c>.</summary>
    Cosine,

    /// <summary>Euclidean (L2) distance — <c>Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions.L2Distance</c>.</summary>
    L2
}
