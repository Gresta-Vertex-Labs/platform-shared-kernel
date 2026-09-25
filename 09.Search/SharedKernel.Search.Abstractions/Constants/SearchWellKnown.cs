namespace SharedKernel.Search.Abstractions.Constants;

/// <summary>
/// Domain-local well-known constants for <c>09.Search</c> — default field names, pagination
/// ceilings, OpenTelemetry source/meter/tag names, and provider name literals.
/// </summary>
/// <remarks>
/// This is the SK0022 named-constant holder for the search domain, mirroring
/// <c>SecurityClaimTypes</c> (<c>12.Security.Abstractions</c>), <c>WebhookSignatureHeaders</c>
/// (<c>15.Integration</c>), and <c>HubGroupNaming</c> (<c>14.Presentation.SignalR</c>). It lives in
/// <c>.Abstractions</c> specifically so both sibling provider packages read byte-identical
/// <see cref="ActivitySourceName"/> / <see cref="MeterName"/> values — that identity is the whole
/// reason <c>13.ServiceDefaults</c> can wire one string name and cover both providers with no
/// <c>ProjectReference</c> to <c>09.Search</c>. The <c>ActivitySource</c> and <c>Meter</c>
/// <em>instances</em> are created per-provider; only the names live here.
/// </remarks>
public static class SearchWellKnown
{
    /// <summary>The default document-id field name used when a caller does not declare one.</summary>
    public const string DefaultPrimaryKeyField = "documentId";

    /// <summary>The default pre-highlight tag (<c>&lt;em&gt;</c>).</summary>
    public const string DefaultHighlightPreTag = "<em>";

    /// <summary>The default post-highlight tag (<c>&lt;/em&gt;</c>).</summary>
    public const string DefaultHighlightPostTag = "</em>";

    /// <summary>The default page size when unspecified.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The maximum page size accepted by the query builder.</summary>
    public const int MaxPageSize = 1000;

    /// <summary>
    /// The default pagination ceiling — pinned to Meilisearch's native default so a query proven
    /// legal on one provider is guaranteed legal on the other. See the "Why MaxTotalHits defaults to
    /// 1000 on both providers" note in <c>09.Search/CLAUDE.md</c>.
    /// </summary>
    public const int DefaultMaxTotalHits = 1000;

    /// <summary>The default facet-value cap.</summary>
    public const int DefaultMaxFacetValues = 100;

    /// <summary>
    /// The shared OpenTelemetry <c>ActivitySource</c> name both provider packages use for their own,
    /// separately-instantiated <c>ActivitySource</c>.
    /// </summary>
    public const string ActivitySourceName = "SharedKernel.Search";

    /// <summary>
    /// The shared OpenTelemetry <c>Meter</c> name both provider packages use for their own,
    /// separately-instantiated <c>Meter</c>.
    /// </summary>
    public const string MeterName = "SharedKernel.Search";

    /// <summary>The provider-name value for Meilisearch.</summary>
    public const string MeilisearchProviderName = "meilisearch";

    /// <summary>The provider-name value for ElasticSearch.</summary>
    public const string ElasticSearchProviderName = "elasticsearch";

    /// <summary>The OpenTelemetry tag key carrying the active provider name (<c>search.provider</c>).</summary>
    public const string ProviderTagName = "search.provider";

    /// <summary>The OpenTelemetry tag key carrying the active index name (<c>search.index</c>).</summary>
    public const string IndexTagName = "search.index";

    /// <summary>
    /// The OpenTelemetry tag key carrying the logical operation name (<c>search.operation</c>) — the
    /// contract member being executed (<c>index</c>, <c>search</c>, <c>count</c>, …), never the
    /// engine's own endpoint path.
    /// </summary>
    public const string OperationTagName = "search.operation";

    /// <summary>
    /// The name of the operation-duration histogram both providers record
    /// (<c>search.client.operation.duration</c>, in seconds), mirroring
    /// <c>storage.client.operation.duration</c> in <c>08.Storage</c>.
    /// </summary>
    public const string OperationDurationMetricName = "search.client.operation.duration";

    /// <summary>
    /// The name of the document-throughput counter both providers record
    /// (<c>search.client.documents</c>), incremented by write and delete operations with the number of
    /// documents the call was asked to affect.
    /// </summary>
    public const string DocumentsMetricName = "search.client.documents";
}
