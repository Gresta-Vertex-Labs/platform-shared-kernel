namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The distance metric a vector collection compares vectors under — declared once, alongside the
/// embedding model identity and dimension, and never a per-query parameter.
/// </summary>
/// <remarks>
/// All three values are genuinely, faithfully supported by both Qdrant (Cosine/Dot/Euclid) and Milvus
/// (COSINE/IP/L2). No fourth metric is offered because a fourth candidate has not been confirmed
/// present on both engines; adding one later requires the same two-engine confirmation this enum
/// received.
/// </remarks>
public enum VectorDistanceMetric
{
    /// <summary>Cosine similarity.</summary>
    Cosine = 0,

    /// <summary>Dot-product similarity (unbounded, magnitude-dependent).</summary>
    DotProduct = 1,

    /// <summary>Euclidean distance (smaller is better — the opposite direction of the other two).</summary>
    Euclidean = 2,
}
