namespace SharedKernel.Search.Meilisearch.Instant;

/// <summary>The Meilisearch term-matching strategy for an <see cref="InstantSearchRequest"/>.</summary>
public enum InstantMatchingStrategy
{
    /// <summary>Progressively drops the least significant terms — Meilisearch's default.</summary>
    Last = 0,

    /// <summary>Every term must match.</summary>
    All = 1,

    /// <summary>Drops the least frequent terms first.</summary>
    Frequency = 2,
}
