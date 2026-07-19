using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Raw;

namespace SharedKernel.Search.ElasticSearch.Extensions;

/// <summary>
/// The fluent builder returned by <c>AddSharedKernelElasticSearchSearch</c> — registers indexes, and
/// opts in to a source-serializer context and raw client access.
/// </summary>
public sealed class ElasticSearchBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, SearchIndexDefinition> _indexDefinitions = new(StringComparer.Ordinal);
    private bool _rawClientAccessAllowed;

    internal ElasticSearchBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Gets the number of indexes registered against this builder so far.</summary>
    internal int RegisteredIndexCount => _indexDefinitions.Count;

    /// <summary>Gets the source-serializer context registered via <see cref="WithSourceSerializerContext"/>, if any.</summary>
    internal JsonSerializerContext? SourceSerializerContext { get; private set; }

    /// <summary>
    /// Registers an index for <typeparamref name="TDocument"/> — scoped <see cref="ISearchIndex{TDocument}"/>,
    /// <see cref="IAnalyticsSearch{TDocument}"/>, and <see cref="ICursorSearch{TDocument}"/>.
    /// </summary>
    /// <param name="readAlias">The alias reads (search/get/count/enumerate) target.</param>
    /// <param name="writeAlias">The alias writes (index/delete/bulk) target.</param>
    /// <param name="configure">Configures the index's field declarations.</param>
    public ElasticSearchBuilder AddIndex<TDocument>(
        string readAlias, string writeAlias, Action<SearchIndexDefinitionBuilder> configure)
        where TDocument : class, ISearchDocument
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(writeAlias);
        ArgumentNullException.ThrowIfNull(configure);

        var definitionBuilder = new SearchIndexDefinitionBuilder(readAlias);
        configure(definitionBuilder);
        var definitionResult = definitionBuilder.Build();
        if (definitionResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to build the SearchIndexDefinition for ElasticSearch index '{readAlias}': {definitionResult.Error.Message}");
        }

        var definition = definitionResult.Value;
        _indexDefinitions[readAlias] = definition;

        _services.AddScoped<ISearchIndex<TDocument>>(sp => new ElasticSearchIndex<TDocument>(
            sp.GetRequiredService<ElasticsearchClient>(),
            definition,
            writeAlias,
            sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value,
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<ElasticSearchIndex<TDocument>>>()));

        _services.AddScoped<IAnalyticsSearch<TDocument>>(sp => new ElasticSearchAnalytics<TDocument>(
            sp.GetRequiredService<ElasticsearchClient>(),
            definition,
            sp.GetRequiredService<ILogger<ElasticSearchAnalytics<TDocument>>>()));

        _services.AddScoped<ICursorSearch<TDocument>>(sp => new ElasticSearchCursorSearch<TDocument>(
            sp.GetRequiredService<ElasticsearchClient>(),
            definition,
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<ElasticSearchCursorSearch<TDocument>>>()));

        return this;
    }

    /// <summary>
    /// Registers a source-generated <see cref="JsonSerializerContext"/> the client's document
    /// (de)serialization is wired through — required for trimmed/AOT consumers, since the client
    /// disables reflection-based STJ by default. Omitting this logs a startup warning.
    /// </summary>
    public ElasticSearchBuilder WithSourceSerializerContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SourceSerializerContext = context;
        return this;
    }

    /// <summary>
    /// Opts in to the last-resort raw client escape hatch (<see cref="IElasticSearchRawClientAccessor"/>).
    /// Logs a startup warning. <b>The raw client bypasses tenant scoping.</b>
    /// </summary>
    public ElasticSearchBuilder AllowRawClientAccess()
    {
        _rawClientAccessAllowed = true;
        return this;
    }

    /// <summary>Finalizes registration and returns the underlying <see cref="IServiceCollection"/>.</summary>
    public IServiceCollection Build()
    {
        var indexDefinitions = new Dictionary<string, SearchIndexDefinition>(_indexDefinitions, StringComparer.Ordinal);

        // Registered via a factory rather than AddSingleton<TInterface, TImplementation>() because
        // ElasticSearchIndexProvisioner's constructor takes a raw ElasticSearchOptions, not
        // IOptions<ElasticSearchOptions> — the only form AddValidatedOptions registers in the
        // container. A plain open-constructor registration would fail to resolve at first use.
        _services.AddSingleton<ISearchIndexProvisioner>(sp => new ElasticSearchIndexProvisioner(
            sp.GetRequiredService<ElasticsearchClient>(),
            sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value,
            sp.GetRequiredService<ILogger<ElasticSearchIndexProvisioner>>()));

        _services.AddSingleton<ISearchProviderDescriptor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value;
            return new ElasticSearchProviderDescriptor(indexDefinitions, options.MaxTotalHits, options.MaxFacetValues);
        });

        if (_rawClientAccessAllowed)
        {
            _services.AddSingleton<IElasticSearchRawClientAccessor>(sp =>
            {
                sp.GetRequiredService<ILogger<ElasticSearchRawClientAccessor>>().ElasticSearchRawClientAccessEnabled();
                return new ElasticSearchRawClientAccessor(sp.GetRequiredService<ElasticsearchClient>());
            });
        }

        return _services;
    }
}
