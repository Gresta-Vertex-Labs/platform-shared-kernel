using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;

namespace SharedKernel.Search.ElasticSearch.Raw;

/// <summary>
/// The last-resort escape hatch onto the raw ElasticSearch SDK client — registered only when the
/// composition root calls <c>.AllowRawClientAccess()</c>, which logs a startup warning.
/// </summary>
/// <remarks>
/// <para>
/// The genuine last resort for capabilities the typed contracts over neutral models cannot reach
/// (e.g. percolators, <c>function_score</c>). The primary provider-exclusive mechanism is
/// <see cref="IAnalyticsSearch{TDocument}"/> and <see cref="ICursorSearch{TDocument}"/> — reach for
/// this only when those are insufficient.
/// </para>
/// <para>
/// <b>THE HATCH BYPASSES TENANT SCOPING.</b> <c>TenantScope</c> injection happens inside the neutral
/// filter compiler; a raw client call receives none of it. A multi-tenant service using this accessor
/// MUST apply its own tenant predicate.
/// </para>
/// </remarks>
public interface IElasticSearchRawClientAccessor
{
    /// <summary>Gets the raw ElasticSearch client.</summary>
    global::Elastic.Clients.Elasticsearch.ElasticsearchClient Client { get; }
}

/// <summary>The default implementation of <see cref="IElasticSearchRawClientAccessor"/>.</summary>
internal sealed class ElasticSearchRawClientAccessor : IElasticSearchRawClientAccessor
{
    /// <summary>Initializes a new <see cref="ElasticSearchRawClientAccessor"/>.</summary>
    public ElasticSearchRawClientAccessor(global::Elastic.Clients.Elasticsearch.ElasticsearchClient client)
    {
        Client = client;
    }

    /// <inheritdoc />
    public global::Elastic.Clients.Elasticsearch.ElasticsearchClient Client { get; }
}
