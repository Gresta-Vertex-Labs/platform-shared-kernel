namespace SharedKernel.Search.Abstractions.Abstractions;

/// <summary>
/// The contract every document type indexed via <c>ISearchIndex&lt;TDocument&gt;</c> must implement —
/// a single, self-supplied document id.
/// </summary>
/// <remarks>
/// <para>
/// The document supplies its own key rather than the package discovering one by reflection or
/// attribute scan — the AOT-preferred, self-supplied-surface pattern the platform already uses for
/// <c>ILoggableRequest&lt;TResponse&gt;</c>, <c>ICacheableQuery.CacheKey</c>, and
/// <c>IInvalidatesCache.CacheKeysToInvalidate</c>. It feeds Meilisearch's <c>primaryKey</c> and
/// ElasticSearch's <c>_id</c> from one string.
/// </para>
/// <para>
/// <b>Charset — the strictest engine is the contract:</b> <see cref="DocumentId"/> must match
/// Meilisearch's constraint of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, hyphen, and underscore only. A
/// violating id is rejected from the write path before any I/O, on both providers.
/// </para>
/// <para>
/// <b>Stability:</b> <see cref="DocumentId"/> must be stable and identical across rebuilds — it is
/// the upsert key on both engines, so an unstable id silently produces duplicates instead of updates.
/// </para>
/// <para>
/// <b>Primitive members only (hard contract rule):</b> an <see cref="ISearchDocument"/> implementation
/// exposes primitive-typed members only (<see cref="string"/>, numeric, <see cref="bool"/>,
/// <see cref="DateTimeOffset"/>, <see cref="Guid"/>, and collections thereof). The Meilisearch SDK
/// declares its <c>JsonSerializerOptions</c> as <c>internal</c>, so there is no seam to register a
/// <c>JsonSerializerContext</c> or a custom converter — a strongly-typed id member round-trips on the
/// ElasticSearch path and does not on the Meilisearch path. This asymmetry cannot be hidden by the
/// abstraction, so it is forbidden at the document-type level instead. Enforcement is documentation
/// plus code review only — a downstream consumer's document types are outside this repo's
/// architecture-test reach.
/// </para>
/// </remarks>
public interface ISearchDocument
{
    /// <summary>Gets this document's stable, unique identifier.</summary>
    string DocumentId { get; }
}
