namespace SharedKernel.Search.Abstractions.Telemetry;

/// <summary>
/// The logical operation names of <c>09.Search</c>, lowercase snake case, used as the <c>search.operation</c> tag and in
/// the span name <c>search {operation}</c>. Both providers emit the same names for the same contract member, so a
/// dashboard keeps working across a provider swap. They name the contract member, never an engine endpoint.
/// </summary>
public static class SearchOperations
{
    /// <summary><c>ISearchIndex.IndexAsync</c>.</summary>
    public const string Index = "index";

    /// <summary><c>ISearchIndex.IndexManyAsync</c>.</summary>
    public const string IndexMany = "index_many";

    /// <summary><c>ISearchIndex.DeleteAsync</c>.</summary>
    public const string Delete = "delete";

    /// <summary><c>ISearchIndex.DeleteManyAsync</c>.</summary>
    public const string DeleteMany = "delete_many";

    /// <summary><c>ISearchIndex.DeleteByFilterAsync</c>.</summary>
    public const string DeleteByFilter = "delete_by_filter";

    /// <summary><c>ISearchIndex.ClearAsync</c>.</summary>
    public const string Clear = "clear";

    /// <summary><c>ISearchIndex.WaitUntilSearchableAsync</c>.</summary>
    public const string WaitUntilSearchable = "wait_until_searchable";

    /// <summary><c>ISearchIndex.SearchAsync</c>.</summary>
    public const string Search = "search";

    /// <summary><c>ISearchIndex.GetAsync</c>.</summary>
    public const string Get = "get";

    /// <summary><c>ISearchIndex.CountAsync</c>.</summary>
    public const string Count = "count";

    /// <summary><c>ISearchIndex.EnumerateAsync</c>.</summary>
    public const string Enumerate = "enumerate";

    /// <summary><c>ISearchIndexProvisioner.EnsureIndexAsync</c>.</summary>
    public const string EnsureIndex = "ensure_index";

    /// <summary><c>ISearchIndexProvisioner.IndexExistsAsync</c>.</summary>
    public const string IndexExists = "index_exists";

    /// <summary><c>ISearchIndexProvisioner.DeleteIndexAsync</c>.</summary>
    public const string DeleteIndex = "delete_index";

    /// <summary><c>ISearchIndexProvisioner.CutoverAsync</c>.</summary>
    public const string Cutover = "cutover";

    /// <summary><c>ISearchIndexProvisioner.VerifyRegisteredIndexesAsync</c>.</summary>
    public const string VerifyIndexes = "verify_indexes";

    /// <summary>The ElasticSearch cursor's point-in-time open (<c>ICursorSearch</c>).</summary>
    public const string OpenCursor = "open_cursor";

    /// <summary>The ElasticSearch cursor's page read (<c>ICursorSearch</c>).</summary>
    public const string ReadCursor = "read_cursor";

    /// <summary>The ElasticSearch cursor's point-in-time close (<c>ICursorSearch</c>).</summary>
    public const string CloseCursor = "close_cursor";

    /// <summary>The ElasticSearch cursor's streamed corpus walk (<c>ICursorSearch.StreamAsync</c>).</summary>
    public const string Stream = "stream";

    /// <summary>The ElasticSearch completion suggester (<c>ISuggestSearch</c>).</summary>
    public const string Suggest = "suggest";

    /// <summary>The ElasticSearch closed aggregation request (<c>IAnalyticsSearch</c>).</summary>
    public const string Aggregate = "aggregate";

    /// <summary>The Meilisearch multi-index instant search (<c>IInstantSearch</c>).</summary>
    public const string InstantSearch = "instant_search";

    /// <summary>The Meilisearch facet-value search (<c>IInstantSearch.SearchFacetValuesAsync</c>).</summary>
    public const string SearchFacetValues = "search_facet_values";

    /// <summary>Meilisearch tenant search-token issuance (<c>ITenantSearchTokenIssuer</c>), no I/O.</summary>
    public const string IssueTenantToken = "issue_tenant_token";
}
