namespace SharedKernel.Persistence.PostgreSQL.Vector;

/// <summary>
/// The pgvector distance/similarity metric used by <see cref="VectorOrderingExpressions.ByDistance{TAggregate}"/>
/// to order a query by proximity to a query vector, and by
/// <see cref="VectorEntityTypeBuilderExtensions.HasVectorIndex{TEntity}"/> to select the matching
/// index operator class.
/// </summary>
/// <remarks>
/// A deliberate narrowing of the six distance/similarity members
/// <c>Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions</c> exposes (<c>CosineDistance</c>/
/// <c>L2Distance</c>/<c>L1Distance</c>/<c>HammingDistance</c>/<c>JaccardDistance</c>/
/// <c>MaxInnerProduct</c>) to the four metrics pgvector's own HNSW/IVFFlat index types actually
/// support an operator class for. <c>Hamming</c>/<c>Jaccard</c> apply only to <c>bit</c>-vector
/// columns, out of scope here.
/// </remarks>
public enum VectorDistanceMetric
{
    /// <summary>Cosine distance — pgvector operator class <c>vector_cosine_ops</c>.</summary>
    Cosine,

    /// <summary>Euclidean (L2) distance — pgvector operator class <c>vector_l2_ops</c>.</summary>
    L2,

    /// <summary>Taxicab (L1/Manhattan) distance — pgvector operator class <c>vector_l1_ops</c>.</summary>
    L1,

    /// <summary>Negative inner product — pgvector operator class <c>vector_ip_ops</c>.</summary>
    InnerProduct,
}
