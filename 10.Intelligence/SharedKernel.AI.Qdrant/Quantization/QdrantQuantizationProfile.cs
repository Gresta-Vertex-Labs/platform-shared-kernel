namespace SharedKernel.AI.Qdrant.Quantization;

/// <summary>
/// The quantization strategy a Qdrant collection is configured with — a Qdrant-exclusive concept with
/// no Milvus equivalent this domain has verified, and therefore declared only in this package.
/// </summary>
public enum QdrantQuantizationProfile
{
    /// <summary>No quantization — vectors are stored and compared at full precision.</summary>
    None = 0,

    /// <summary>Scalar quantization.</summary>
    Scalar = 1,

    /// <summary>Binary quantization.</summary>
    Binary = 2,

    /// <summary>Product quantization.</summary>
    Product = 3,
}
