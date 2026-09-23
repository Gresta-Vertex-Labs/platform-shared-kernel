using System.Globalization;

namespace SharedKernel.Search.Meilisearch.Provisioning;

/// <summary>
/// One entry in a Meilisearch index's ordered ranking-rule list — the engine's relevance-ordering
/// configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Declared here, in the Meilisearch package, and never in
/// <c>SharedKernel.Search.Abstractions</c>.</b> Ranking rules are an ordered list of tie-breakers
/// Meilisearch applies in sequence to decide relevance order, and they are configured per index at
/// provisioning time. ElasticSearch has no counterpart: its ordering comes from BM25 scoring plus
/// per-query boosts and <c>function_score</c>, which is a different mechanism applied at a different
/// time and expressible only per query. There is no faithful translation in either direction, and a
/// neutral ranking-rule surface would mean one adapter silently ignoring the caller's ordering intent
/// while the other honoured it. Placing the type here means a call site that configures ranking rules
/// takes a compile-time dependency on this package, so swapping providers surfaces as a build error
/// enumerating every non-portable site — the same mechanism <c>IInstantSearch&lt;TDocument&gt;</c> and
/// <c>ITenantSearchTokenIssuer</c> already use.
/// </para>
/// <para>
/// <b>Typed rather than raw strings.</b> Meilisearch's API takes these as bare strings
/// (<c>"words"</c>, <c>"typo"</c>, <c>"price:asc"</c>). A misspelling is accepted by the engine's
/// settings endpoint in some versions and silently changes relevance in others — a failure mode with no
/// symptom except "search results feel wrong". The factories below are the only construction path, so a
/// rule is either well-formed or does not compile.
/// </para>
/// <para>
/// <b>Order is meaning.</b> Meilisearch applies the rules in the order supplied, so the list is a
/// priority ranking, not a set. Supplying a partial list replaces the engine's default sequence
/// entirely — include every rule you still want, in the order you want it.
/// </para>
/// </remarks>
public sealed record MeilisearchRankingRule
{
    private MeilisearchRankingRule(string value)
    {
        Value = value;
    }

    /// <summary>Gets the raw rule string sent to Meilisearch's settings endpoint.</summary>
    public string Value { get; }

    /// <summary>Number of query terms present in the document, descending — Meilisearch's <c>words</c>.</summary>
    public static MeilisearchRankingRule Words { get; } = new("words");

    /// <summary>Number of typos between the query and the match, ascending — Meilisearch's <c>typo</c>.</summary>
    public static MeilisearchRankingRule Typo { get; } = new("typo");

    /// <summary>Distance between matched query terms, ascending — Meilisearch's <c>proximity</c>.</summary>
    public static MeilisearchRankingRule Proximity { get; } = new("proximity");

    /// <summary>
    /// Position of the match in the <c>searchableAttributes</c> order — Meilisearch's
    /// <c>attribute</c>. This is the rule that makes a title match outrank a description match, and it
    /// derives its priority from the order fields were declared searchable in.
    /// </summary>
    public static MeilisearchRankingRule Attribute { get; } = new("attribute");

    /// <summary>
    /// Honours the query's own sort parameters at this point in the sequence — Meilisearch's
    /// <c>sort</c>. Its position decides whether an explicit sort outranks relevance or merely breaks
    /// ties within it.
    /// </summary>
    public static MeilisearchRankingRule Sort { get; } = new("sort");

    /// <summary>Exactness of the term match, descending — Meilisearch's <c>exactness</c>.</summary>
    public static MeilisearchRankingRule Exactness { get; } = new("exactness");

    /// <summary>
    /// A custom ascending rule on <paramref name="field"/> (Meilisearch's <c>{field}:asc</c>). The
    /// field must be declared sortable on the index definition.
    /// </summary>
    public static MeilisearchRankingRule Ascending(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new MeilisearchRankingRule(string.Create(CultureInfo.InvariantCulture, $"{field}:asc"));
    }

    /// <summary>
    /// A custom descending rule on <paramref name="field"/> (Meilisearch's <c>{field}:desc</c>). The
    /// field must be declared sortable on the index definition.
    /// </summary>
    public static MeilisearchRankingRule Descending(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return new MeilisearchRankingRule(string.Create(CultureInfo.InvariantCulture, $"{field}:desc"));
    }

    /// <summary>Returns <see cref="Value"/>, the raw rule string.</summary>
    public override string ToString() => Value;
}
