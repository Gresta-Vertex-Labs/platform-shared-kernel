using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Raw;
using SharedKernel.Search.ElasticSearch.Suggest;

namespace SharedKernel.Search.ElasticSearch.Extensions;

/// <summary>
/// The fluent builder returned by <c>AddSharedKernelElasticSearchSearch</c> — registers indexes, and
/// opts in to a source-serializer context and raw client access.
/// </summary>
public sealed class ElasticSearchBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, SearchIndexDefinition> _indexDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _completionFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _writeAliases = new(StringComparer.Ordinal);
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
        _writeAliases[readAlias] = writeAlias;

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
    /// Declares a <c>completion</c>-typed suggest field on the already-registered index named
    /// <paramref name="indexName"/>, and registers <see cref="ISuggestSearch{TDocument}"/> for it — an
    /// ElasticSearch-exclusive capability with no Meilisearch counterpart.
    /// </summary>
    /// <typeparam name="TDocument">The document type registered for <paramref name="indexName"/>.</typeparam>
    /// <param name="indexName">The read alias the index was registered under.</param>
    /// <param name="suggestField">
    /// The name of the completion field to add to the mapping. It is a separate field from the source
    /// text it completes, because a completion field is an FST input, not a searchable text field.
    /// </param>
    /// <remarks>
    /// <para>
    /// A completion field must exist in the mapping <em>before</em> documents are indexed, so this is a
    /// provisioning-time declaration rather than a query option, and adding it to a populated index
    /// requires a staging rebuild and a cutover for existing documents to become suggestable. On a
    /// tenanted index the tenant field is registered as a category context on the completion mapping —
    /// the completion suggester ignores query filters entirely, so a context is the only mechanism that
    /// can scope a suggestion to one tenant.
    /// </para>
    /// <para>
    /// The document type must populate this field itself (typically with the same value as the display
    /// name it completes, plus any alternative inputs). Repeated calls for the same index accumulate.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="indexName"/> has not been registered with <see cref="AddIndex{TDocument}"/>.
    /// </exception>
    public ElasticSearchBuilder WithCompletionField<TDocument>(string indexName, string suggestField)
        where TDocument : class, ISearchDocument
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestField);

        if (!_indexDefinitions.TryGetValue(indexName, out var definition))
        {
            throw new InvalidOperationException(
                $"Cannot declare a completion field on ElasticSearch index '{indexName}': it has not been " +
                "registered. Call AddIndex<TDocument>(...) for this index first.");
        }

        if (!_completionFields.TryGetValue(indexName, out var fields))
        {
            fields = new List<string>();
            _completionFields[indexName] = fields;
        }

        if (!fields.Contains(suggestField, StringComparer.Ordinal))
        {
            fields.Add(suggestField);
        }

        // Re-registered on every call so the scoped ISuggestSearch<TDocument> closes over the complete
        // field list, not the partial one that existed at the first call.
        var snapshot = fields.ToArray();
        _services.AddScoped<ISuggestSearch<TDocument>>(sp => new ElasticSearchSuggestSearch<TDocument>(
            sp.GetRequiredService<ElasticsearchClient>(),
            definition,
            snapshot,
            sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value,
            sp.GetRequiredService<ILogger<ElasticSearchSuggestSearch<TDocument>>>()));

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
        var completionFields = _completionFields.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value.ToArray(),
            StringComparer.Ordinal);

        var writeAliases = new Dictionary<string, string>(_writeAliases, StringComparer.Ordinal);

        // Registered BOTH keyed and unkeyed, with the unkeyed registration resolving the keyed one so
        // there is exactly one instance either way — this provisioner caches probe results, so two
        // instances would mean two caches disagreeing with each other.
        //
        // The key exists because these two contracts are non-generic. A host running both engines gets
        // last-registration-wins on the unkeyed resolution, and the distinct-TDocument rule does NOT
        // help: it disambiguates ISearchIndex<TDocument> and nothing else.
        _services.AddKeyedSingleton<ISearchIndexProvisioner>(
            SearchWellKnown.ElasticSearchProviderName,
            (sp, _) => new ElasticSearchIndexProvisioner(
                sp.GetRequiredService<ElasticsearchClient>(),
                sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value,
                indexDefinitions,
                completionFields,
                writeAliases,
                sp.GetRequiredService<ILogger<ElasticSearchIndexProvisioner>>()));

        _services.AddSingleton<ISearchIndexProvisioner>(sp =>
            sp.GetRequiredKeyedService<ISearchIndexProvisioner>(SearchWellKnown.ElasticSearchProviderName));

        // One readiness probe per registered index, each over the one provisioner instance above.
        foreach (var indexName in indexDefinitions.Keys)
        {
            _services.AddReadinessProbe(sp =>
            {
                var provisioner = (ElasticSearchIndexProvisioner)sp.GetRequiredKeyedService<ISearchIndexProvisioner>(
                    SearchWellKnown.ElasticSearchProviderName);
                return new SearchIndexReadinessProbe(SearchWellKnown.ElasticSearchProviderName, indexName, provisioner.ProbeAsync);
            });
        }

        _services.AddKeyedSingleton<ISearchProviderDescriptor>(
            SearchWellKnown.ElasticSearchProviderName,
            (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value;
                return new ElasticSearchProviderDescriptor(indexDefinitions, options.MaxTotalHits, options.MaxFacetValues);
            });

        _services.AddSingleton<ISearchProviderDescriptor>(sp =>
            sp.GetRequiredKeyedService<ISearchProviderDescriptor>(SearchWellKnown.ElasticSearchProviderName));

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
