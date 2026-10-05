namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The consistency a caller requires from a write on <c>ISearchIndex&lt;TDocument&gt;</c> — mandatory
/// and non-defaulted on every write method.
/// </summary>
/// <remarks>
/// A bare fire-and-forget write is dishonest on both engines, in two different ways. ElasticSearch
/// returns once the document is durable in the translog but not searchable until the next refresh
/// (<c>index.refresh_interval</c>, default 1s). Meilisearch returns only a <c>taskUid</c> in state
/// <c>enqueued</c>, processed by a single global sequential queue — under load the delay to
/// searchability is effectively unbounded, and a different index's backlog delays yours. There is
/// deliberately no third value exposing ElasticSearch's <c>refresh=true</c> — it forces an immediate
/// cluster-wide refresh that disturbs other in-flight requests and has no Meilisearch analogue.
/// </remarks>
public enum SearchWriteConsistency
{
    /// <summary>
    /// The write is durable (ElasticSearch) or enqueued (Meilisearch) but not yet guaranteed
    /// searchable.
    /// </summary>
    Accepted = 0,

    /// <summary>The provider waits until the write is visible to search before returning.</summary>
    Searchable = 1,
}
