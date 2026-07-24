namespace SharedKernel.AI.Qdrant.Constants;

/// <summary>
/// Domain-local well-known constants for <c>SharedKernel.AI.Qdrant</c> — reserved payload keys,
/// the fingerprint metadata key, and default provisioning values.
/// </summary>
/// <remarks>
/// This is the <c>SK0022</c> named-constant holder for this provider package, mirroring
/// <c>IntelligenceWellKnown</c> (<c>SharedKernel.AI.Abstractions</c>) and <c>SearchWellKnown</c>
/// (<c>09.Search.Abstractions</c>).
/// </remarks>
internal static class QdrantWellKnown
{
    /// <summary>
    /// The reserved point-payload key under which a record's embedding model identity (<c>IVectorRecord.ModelId</c>)
    /// is stored, since a Qdrant point payload has no separate "model id" slot alongside its vector —
    /// every point's <c>ModelId</c> is stored here so it round-trips on read.
    /// </summary>
    public const string ModelIdPayloadKey = "__sk_vector_model_id";

    /// <summary>
    /// The collection-metadata key under which <c>VectorCollectionDefinition.Fingerprint</c> is
    /// persisted — via <c>Qdrant.Client</c> 1.18.1's genuine collection-level <c>metadata</c> map
    /// (<c>CreateCollectionAsync</c>/<c>UpdateCollectionAsync</c>'s <c>metadata</c> parameter), confirmed
    /// present via direct assembly reflection at Core-phase implementation time. This supersedes the
    /// Design-phase assumption that Qdrant has no collection-level metadata slot and a reserved sentinel
    /// point would be required — the real, current API makes that workaround unnecessary.
    /// </summary>
    public const string FingerprintMetadataKey = "sk_schema_fingerprint";

    /// <summary>The default gRPC port <c>Qdrant.Client</c> connects to.</summary>
    public const int DefaultPort = 6334;
}
