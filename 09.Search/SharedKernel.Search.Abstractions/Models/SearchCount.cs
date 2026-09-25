namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The result of <c>ISearchIndex&lt;TDocument&gt;.CountAsync</c> — a document count together with the
/// <see cref="TotalHitsAccuracy"/> qualifier that says whether it may be trusted as exact.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists rather than a bare <see cref="long"/>:</b> the two engines cannot both
/// answer "how many documents match?" exactly. ElasticSearch has a real <c>_count</c> API that returns
/// an uncapped, exact figure. Meilisearch has no count endpoint at all — the only way to obtain a
/// total is to read <c>totalHits</c> off a paginated search, and Meilisearch caps that value at the
/// index's own <c>pagination.maxTotalHits</c> setting (which this platform provisions from
/// <see cref="SearchIndexDefinition.MaxTotalHits"/>, default
/// <see cref="Constants.SearchWellKnown.DefaultMaxTotalHits"/>). A bare <see cref="long"/> return
/// therefore published a silently truncated number as fact on one provider and the truth on the other
/// — the precise silent-divergence defect class this domain's whole design exists to prevent.
/// </para>
/// <para>
/// <b>Read <see cref="Accuracy"/> before trusting <see cref="Value"/>.</b>
/// <see cref="TotalHitsAccuracy.Exact"/> means the figure is the real number of matching documents.
/// <see cref="TotalHitsAccuracy.LowerBound"/> means at least this many documents match and the engine
/// stopped counting — on Meilisearch this is what a count that reached the index ceiling reports, so
/// <c>Value == MaxTotalHits</c> and the true total is unknown and larger. Use
/// <see cref="IsExact"/> for the common branch.
/// </para>
/// <para>
/// This type deliberately reuses <see cref="TotalHitsAccuracy"/> rather than declaring a second
/// accuracy vocabulary — a count and a result-set total are the same question asked two ways, and one
/// enum keeps a consumer's handling of both identical.
/// </para>
/// </remarks>
public readonly record struct SearchCount
{
    private SearchCount(long value, TotalHitsAccuracy accuracy)
    {
        Value = value;
        Accuracy = accuracy;
    }

    /// <summary>Gets the document count, qualified by <see cref="Accuracy"/>.</summary>
    public long Value { get; }

    /// <summary>Gets the accuracy qualifier for <see cref="Value"/>.</summary>
    public TotalHitsAccuracy Accuracy { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Value"/> is the exact number of matching documents.
    /// </summary>
    public bool IsExact => Accuracy == TotalHitsAccuracy.Exact;

    /// <summary>Creates an exact count.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static SearchCount Exact(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new SearchCount(value, TotalHitsAccuracy.Exact);
    }

    /// <summary>
    /// Creates a lower-bound count — at least <paramref name="value"/> documents match, and the engine
    /// stopped counting there.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static SearchCount AtLeast(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new SearchCount(value, TotalHitsAccuracy.LowerBound);
    }

    /// <summary>
    /// Returns a diagnostic string that shows the accuracy alongside the figure, so a truncated count
    /// is never mistaken for an exact one in a log line or an assertion failure message.
    /// </summary>
    public override string ToString() => Accuracy == TotalHitsAccuracy.Exact
        ? Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $">={Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
