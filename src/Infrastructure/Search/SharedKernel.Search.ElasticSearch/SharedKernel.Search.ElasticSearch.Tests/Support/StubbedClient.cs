using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// Builds <see cref="ElasticsearchClient"/> instances over the real transport and the real
/// <see cref="HttpRequestInvoker"/>, with a <see cref="ScriptedRequestInvoker"/> as the HTTP handler — no
/// container, no network.
/// </summary>
internal static class StubbedClient
{
    public static ElasticsearchClient Create(ScriptedRequestInvoker handler, Func<ElasticsearchClientSettings, ElasticsearchClientSettings>? configure = null)
    {
        var invoker = new HttpRequestInvoker((_, _) => handler);
        var settings = new ElasticsearchClientSettings(new SingleNodePool(new Uri("http://es-host.sentinel.invalid:9200")), invoker)
            .DisablePing();
        return new ElasticsearchClient(configure is null ? settings : configure(settings));
    }
}
