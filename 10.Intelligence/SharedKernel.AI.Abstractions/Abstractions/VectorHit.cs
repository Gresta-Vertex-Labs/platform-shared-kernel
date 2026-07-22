using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>One ranked result of a <see cref="VectorQuery"/>.</summary>
/// <remarks>
/// <para>
/// <b><see cref="Score"/> exists — the one deliberate deviation from <c>09.Search</c>'s outright score
/// ban, decided, not defaulted:</b> similarity score is often genuinely load-bearing for a RAG caller
/// (e.g. "only surface chunks above cosine 0.75") in a way full-text relevance rarely is. It is kept,
/// with the loudest possible warning on the member itself:
/// </para>
/// <para>
/// <b>THE SCALE OF <see cref="Score"/> IS PROVIDER- AND METRIC-SPECIFIC.</b> Cosine similarity is
/// bounded [-1, 1] (or [0, 1] depending on normalisation); dot-product is unbounded and depends on
/// vector magnitude; Euclidean distance is smaller-is-better, the opposite direction of the other two.
/// A threshold tuned against one provider/metric pairing is silently meaningless against another —
/// <see cref="Score"/> must not be persisted, compared across a provider swap, or compared across a
/// <see cref="VectorDistanceMetric"/> change, without re-deriving the threshold empirically against the
/// new pairing.
/// </para>
/// <para>
/// <see cref="Rank"/> (the 0-based ordinal within this result page) is the portable substitute for
/// callers who want ordering without touching <see cref="Score"/> at all, mirroring <c>09.Search</c>
/// exactly.
/// </para>
/// </remarks>
public sealed record VectorHit<TRecord>
    where TRecord : class, IVectorRecord
{
    /// <summary>Gets the matched record.</summary>
    public required TRecord Record { get; init; }

    /// <summary>
    /// Gets the raw similarity/distance score, whose scale is provider- and metric-specific. See the
    /// type-level remarks — never compare, threshold, or persist this value across a provider or
    /// metric change without re-deriving the threshold empirically.
    /// </summary>
    public required float Score { get; init; }

    /// <summary>Gets the 0-based ordinal of this hit within its result page — the portable ordering signal.</summary>
    public required int Rank { get; init; }
}
