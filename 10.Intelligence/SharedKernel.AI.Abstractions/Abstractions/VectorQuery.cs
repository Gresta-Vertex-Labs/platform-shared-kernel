using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>A similarity query against a vector collection.</summary>
/// <remarks>
/// <para>
/// <b><see cref="Vector"/> and <see cref="ModelId"/> are both required, non-defaulted:</b> unlike a
/// full-text search request's optional free-text member, a similarity query with no query vector is not
/// a coherent operation in this domain at all. <see cref="ModelId"/> is validated against
/// <c>VectorCollectionDefinition.EmbeddingModelId</c> before any I/O, identically to the write path.
/// </para>
/// <para>
/// <b><see cref="MinScore"/> is documented as provider-and-metric-specific</b>, the same warning as
/// <see cref="VectorHit{TRecord}.Score"/>: it is applied server-side where the engine supports a score
/// threshold, and validated for directional sanity per <see cref="VectorDistanceMetric"/> where
/// feasible.
/// </para>
/// <para>
/// <b><see cref="ReturnVector"/> defaults <see langword="false"/>:</b> vectors are large (a 1536-dim
/// float32 vector is 6 KB) and most callers only need metadata and score. Opt-in keeps the default
/// response cheap.
/// </para>
/// <para>
/// <b>No free-text member — there is no analogue here:</b> unlike a full-text search request's
/// <c>FreeText</c>, there is no "text" concept in a vector query distinct from <see cref="Vector"/>
/// itself — whatever text the caller wants matched <em>is</em> what produced <see cref="Vector"/> via
/// <see cref="IEmbeddingGenerator"/> upstream of this call.
/// </para>
/// </remarks>
public sealed record VectorQuery
{
    /// <summary>Gets the query vector.</summary>
    public required ReadOnlyMemory<float> Vector { get; init; }

    /// <summary>Gets the identifier of the embedding model that produced <see cref="Vector"/>.</summary>
    public required string ModelId { get; init; }

    /// <summary>Gets the structured metadata filter, or <see langword="null"/> for no filter.</summary>
    public VectorFilter? Filter { get; init; }

    /// <summary>Gets the maximum number of hits to return.</summary>
    public int Limit { get; init; } = IntelligenceWellKnown.DefaultQueryLimit;

    /// <summary>
    /// Gets the minimum score threshold, or <see langword="null"/> for no threshold. The scale is
    /// provider- and metric-specific — see the type-level remarks.
    /// </summary>
    public float? MinScore { get; init; }

    /// <summary>Gets a value indicating whether each hit's metadata should be populated.</summary>
    public bool ReturnMetadata { get; init; } = true;

    /// <summary>Gets a value indicating whether each hit's raw vector should be populated. Defaults to <see langword="false"/>.</summary>
    public bool ReturnVector { get; init; }
}
