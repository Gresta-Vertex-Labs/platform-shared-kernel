using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Quantization;

/// <summary>
/// A Qdrant-exclusive read-only accessor for the active quantization profile configured on a
/// collection — declared only in this package, never <c>SharedKernel.AI.Abstractions</c>, since
/// quantization-profile access is a Qdrant-specific storage-optimization capability this domain has
/// not verified as honestly implementable against Milvus.
/// </summary>
public interface IQdrantQuantizationProfileAccessor
{
    /// <summary>Gets the quantization profile currently configured on <paramref name="collectionName"/>.</summary>
    Task<Result<QdrantQuantizationProfile>> GetQuantizationProfileAsync(
        string collectionName,
        CancellationToken cancellationToken = default);
}
