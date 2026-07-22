namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The self-supplied surface every <c>TRecord</c> implements to participate in
/// <see cref="Abstractions.IVectorCollection{TRecord}"/> — no reflection, no attribute scan, mirroring
/// <c>ISearchDocument</c> / <c>ILoggableRequest&lt;TResponse&gt;</c> / <c>ICacheableQuery.CacheKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>TRecord</c> in this domain is constrained <c>where TRecord : class, IVectorRecord</c>.
/// <see cref="ModelId"/> is the embedding-model identity that produced <see cref="Vector"/> — carried
/// on the record itself (not inferred from <see cref="Vector"/>'s length, which cannot distinguish two
/// different models that happen to share a dimension) so the adapter can validate it against
/// <see cref="VectorCollectionDefinition.EmbeddingModelId"/> before any I/O. This is the direct
/// implementation of this domain's sharpest invariant: no engine can detect a model-identity
/// mismatch — the contract detects it instead.
/// </para>
/// <para>
/// <b><see cref="Id"/> stability and charset:</b> <see cref="Id"/> must be stable and identical across
/// re-embeds — it is the upsert key on both providers. A violating id returns
/// <c>IntelligenceErrors.InvalidRecordId</c> before any I/O on both providers — the concrete charset is
/// each provider's own translator responsibility, confirmed against its real, current API at that
/// provider's Core phase.
/// </para>
/// <para>
/// <b><see cref="Metadata"/> is the entire portable payload surface:</b> there is no separate
/// "content"/"text" member — whatever text was embedded to produce <see cref="Vector"/>, if the caller
/// wants it retrievable, is stored as a <see cref="Metadata"/> entry like any other field. This keeps
/// the shape uniform across every use case (RAG chunk, entity embedding, image caption embedding)
/// rather than privileging one.
/// </para>
/// </remarks>
public interface IVectorRecord
{
    /// <summary>Gets the record's stable, provider-charset-legal identifier — the upsert key.</summary>
    string Id { get; }

    /// <summary>Gets the embedding vector.</summary>
    ReadOnlyMemory<float> Vector { get; }

    /// <summary>Gets the identifier of the embedding model that produced <see cref="Vector"/>.</summary>
    string ModelId { get; }

    /// <summary>Gets the record's portable metadata payload, keyed by declared field name.</summary>
    IReadOnlyDictionary<string, VectorValue> Metadata { get; }
}
