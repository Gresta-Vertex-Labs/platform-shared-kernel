namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// A request to atomically cut a staging index over to serve as the live index —
/// <c>ISearchIndexProvisioner.CutoverAsync</c>.
/// </summary>
/// <remarks>
/// One neutral name over two mechanics: ElasticSearch issues a single <c>_aliases</c> request
/// containing both the remove and the add, applied atomically, so the read alias is never undefined —
/// <see cref="LiveIndexName"/> is therefore an alias. Meilisearch has no aliases and calls
/// <c>POST /swap-indexes</c>, which atomically swaps documents, settings, and task history —
/// <see cref="LiveIndexName"/> is a real index name, and after the swap the staging name still exists
/// holding the old data. <see cref="DeleteStagingAfterCutover"/> exists precisely to normalise that;
/// without it, every Meilisearch rebuild silently doubles storage.
/// </remarks>
public sealed record IndexCutoverRequest
{
    /// <summary>Gets the name of the staging index (already populated with new data).</summary>
    public required string StagingIndexName { get; init; }

    /// <summary>Gets the name (ElasticSearch: alias; Meilisearch: real index name) callers query.</summary>
    public required string LiveIndexName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the staging index should be deleted after a successful
    /// cutover. Defaults to <see langword="true"/> — on Meilisearch, declining this leaves the
    /// staging name holding the pre-cutover data, silently doubling storage.
    /// </summary>
    public bool DeleteStagingAfterCutover { get; init; } = true;
}
