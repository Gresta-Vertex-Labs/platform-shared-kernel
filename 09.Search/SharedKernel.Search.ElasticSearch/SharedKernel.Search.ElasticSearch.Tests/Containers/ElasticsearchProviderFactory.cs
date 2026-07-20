using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.Containers;

/// <summary>
/// Builds real, non-DI <see cref="ElasticsearchClient"/>/<see cref="ElasticSearchIndex{TDocument}"/>/
/// <see cref="ElasticSearchIndexProvisioner"/>/<see cref="ElasticSearchAnalytics{TDocument}"/>/
/// <see cref="ElasticSearchCursorSearch{TDocument}"/> instances wired directly against a running
/// <see cref="ElasticsearchContainerFixture"/> — the construction shape
/// <c>AddSharedKernelElasticSearchSearch</c> itself uses (see
/// <c>Extensions/ElasticSearchServiceCollectionExtensions.cs</c>), minus the DI container, so
/// behavioral/round-trip tests exercise the exact same <see cref="ElasticsearchClient"/> configuration
/// a consuming host would build.
/// </summary>
internal static class ElasticsearchProviderFactory
{
    /// <summary>Creates a default <see cref="ElasticSearchOptions"/> instance for real-backend tests, optionally customized.</summary>
    public static ElasticSearchOptions CreateOptions(Action<ElasticSearchOptions>? configure = null)
    {
        var options = new ElasticSearchOptions();
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Creates a real <see cref="ElasticsearchClient"/> pointed at <paramref name="fixture"/>'s running container.</summary>
    public static ElasticsearchClient CreateClient(ElasticsearchContainerFixture fixture)
    {
        var nodePool = new SingleNodePool(new Uri(fixture.Nodes[0]));
        var settings = new ElasticsearchClientSettings(nodePool)
            .Authentication(new BasicAuthentication(fixture.Username, fixture.Password));

        if (fixture.AllowInvalidCertificates)
        {
            settings = settings.ServerCertificateValidationCallback((_, _, _, _) => true);
        }

        return new ElasticsearchClient(settings);
    }

    /// <summary>
    /// Creates an <see cref="ElasticSearchIndex{TDocument}"/> targeting <paramref name="definition"/>,
    /// defaulting the write alias to <paramref name="definition"/>'s own name (the common case where no
    /// cutover-driven alias split is in play) and every other dependency to a real-but-inert double.
    /// </summary>
    public static ElasticSearchIndex<TDocument> CreateIndex<TDocument>(
        ElasticsearchClient client,
        SearchIndexDefinition definition,
        string? writeAlias = null,
        ElasticSearchOptions? options = null,
        IClock? clock = null,
        ILogger<ElasticSearchIndex<TDocument>>? logger = null)
        where TDocument : class, ISearchDocument
        => new(
            client,
            definition,
            writeAlias ?? definition.Name,
            options ?? CreateOptions(),
            clock ?? new FakeClock(),
            logger ?? NullLogger<ElasticSearchIndex<TDocument>>.Instance);

    /// <summary>Creates an <see cref="ElasticSearchIndexProvisioner"/> against <paramref name="client"/>.</summary>
    public static ElasticSearchIndexProvisioner CreateProvisioner(
        ElasticsearchClient client, ElasticSearchOptions? options = null, ILogger<ElasticSearchIndexProvisioner>? logger = null)
        => new(client, options ?? CreateOptions(), logger ?? NullLogger<ElasticSearchIndexProvisioner>.Instance);

    /// <summary>Creates an <see cref="ElasticSearchAnalytics{TDocument}"/> against <paramref name="client"/>.</summary>
    public static ElasticSearchAnalytics<TDocument> CreateAnalytics<TDocument>(
        ElasticsearchClient client, SearchIndexDefinition definition, ILogger<ElasticSearchAnalytics<TDocument>>? logger = null)
        where TDocument : class, ISearchDocument
        => new(client, definition, logger ?? NullLogger<ElasticSearchAnalytics<TDocument>>.Instance);

    /// <summary>Creates an <see cref="ElasticSearchCursorSearch{TDocument}"/> against <paramref name="client"/>.</summary>
    public static ElasticSearchCursorSearch<TDocument> CreateCursorSearch<TDocument>(
        ElasticsearchClient client,
        SearchIndexDefinition definition,
        IClock? clock = null,
        ILogger<ElasticSearchCursorSearch<TDocument>>? logger = null)
        where TDocument : class, ISearchDocument
        => new(client, definition, clock ?? new FakeClock(), logger ?? NullLogger<ElasticSearchCursorSearch<TDocument>>.Instance);
}
