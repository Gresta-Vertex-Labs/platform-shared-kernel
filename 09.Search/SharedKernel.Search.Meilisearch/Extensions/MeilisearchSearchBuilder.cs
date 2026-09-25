using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Diagnostics;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Instant;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.Search.Meilisearch.Raw;
using SharedKernel.Search.Meilisearch.Tenancy;

namespace SharedKernel.Search.Meilisearch.Extensions;

/// <summary>
/// The fluent builder returned by <c>AddSharedKernelMeilisearchSearch</c> — registers indexes, and
/// opts in to tenant tokens and raw client access.
/// </summary>
public sealed class MeilisearchSearchBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, SearchIndexDefinition> _indexDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _rankingRules = new(StringComparer.Ordinal);
    private bool _tenantTokensRequested;
    private bool _rawClientAccessAllowed;

    internal MeilisearchSearchBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Gets the number of indexes registered against this builder so far.</summary>
    internal int RegisteredIndexCount => _indexDefinitions.Count;

    /// <summary>
    /// Registers an index for <typeparamref name="TDocument"/> — scoped
    /// <see cref="ISearchIndex{TDocument}"/> and <see cref="IInstantSearch{TDocument}"/>.
    /// </summary>
    public MeilisearchSearchBuilder AddIndex<TDocument>(string indexName, Action<SearchIndexDefinitionBuilder> configure)
        where TDocument : class, ISearchDocument
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentNullException.ThrowIfNull(configure);

        var definitionBuilder = new SearchIndexDefinitionBuilder(indexName);
        configure(definitionBuilder);
        var definitionResult = definitionBuilder.Build();
        if (definitionResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to build the SearchIndexDefinition for Meilisearch index '{indexName}': {definitionResult.Error.Message}");
        }

        var definition = definitionResult.Value;
        _indexDefinitions[indexName] = definition;

        _services.AddScoped<ISearchIndex<TDocument>>(sp => new MeilisearchIndex<TDocument>(
            sp.GetRequiredService<global::Meilisearch.MeilisearchClient>(),
            definition,
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<MeilisearchIndex<TDocument>>>()));

        _services.AddScoped<IInstantSearch<TDocument>>(sp => new MeilisearchInstantSearch<TDocument>(
            sp.GetRequiredService<global::Meilisearch.MeilisearchClient>(),
            definition,
            sp.GetRequiredService<ILogger<MeilisearchInstantSearch<TDocument>>>()));

        return this;
    }

    /// <summary>
    /// Declares the ordered ranking-rule sequence Meilisearch applies to the already-registered index
    /// named <paramref name="indexName"/> — a Meilisearch-exclusive relevance control with no
    /// ElasticSearch counterpart.
    /// </summary>
    /// <remarks>
    /// Order is meaning: the rules are applied in the sequence given, and supplying a list replaces the
    /// engine's default sequence entirely rather than adding to it. See
    /// <see cref="MeilisearchRankingRule"/> for why this lives in the provider package. Call it after
    /// the matching <see cref="AddIndex{TDocument}"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="indexName"/> has not been registered with <see cref="AddIndex{TDocument}"/> —
    /// ranking rules for an unregistered index would be silently discarded at provisioning time.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="rules"/> is empty.</exception>
    public MeilisearchSearchBuilder WithRankingRules(string indexName, params MeilisearchRankingRule[] rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Length == 0)
        {
            throw new ArgumentException(
                "At least one ranking rule is required; an empty list would clear Meilisearch's ranking rules entirely.",
                nameof(rules));
        }

        if (!_indexDefinitions.ContainsKey(indexName))
        {
            throw new InvalidOperationException(
                $"Cannot set ranking rules for Meilisearch index '{indexName}': it has not been registered. " +
                "Call AddIndex<TDocument>(...) for this index first.");
        }

        _rankingRules[indexName] = rules.Select(rule => rule.Value).ToArray();
        return this;
    }

    /// <summary>Opts in to engine-enforced per-tenant search tokens via <see cref="ITenantSearchTokenIssuer"/>.</summary>
    public MeilisearchSearchBuilder WithTenantTokens()
    {
        _tenantTokensRequested = true;
        return this;
    }

    /// <summary>
    /// Opts in to the last-resort raw client escape hatch (<see cref="IMeilisearchRawClientAccessor"/>).
    /// Logs a startup warning. <b>The raw client bypasses tenant scoping.</b>
    /// </summary>
    public MeilisearchSearchBuilder AllowRawClientAccess()
    {
        _rawClientAccessAllowed = true;
        return this;
    }

    /// <summary>Finalizes registration and returns the underlying <see cref="IServiceCollection"/>.</summary>
    public IServiceCollection Build()
    {
        var indexDefinitions = new Dictionary<string, SearchIndexDefinition>(_indexDefinitions, StringComparer.Ordinal);

        // Registered via a factory rather than AddSingleton<TInterface, TImplementation>() because
        // MeilisearchIndexProvisioner's constructor takes a raw MeilisearchOptions, not
        // IOptions<MeilisearchOptions> — the only form AddValidatedOptions registers in the container.
        // A plain open-constructor registration would fail to resolve at first use.
        var rankingRules = new Dictionary<string, IReadOnlyList<string>>(_rankingRules, StringComparer.Ordinal);

        // Registered BOTH keyed and unkeyed, with the unkeyed registration resolving the keyed one so
        // there is exactly one instance either way — the provisioner holds per-index state, so two
        // instances would mean two probe caches disagreeing with each other.
        //
        // The key exists because these two contracts are non-generic. A host running both engines —
        // which this domain's own README markets as the point of having two providers — gets
        // last-registration-wins on the unkeyed resolution, and the distinct-TDocument rule does NOT
        // help: it disambiguates ISearchIndex<TDocument> and nothing else. That shadowing is silent and
        // its symptom is remote from its cause: a readiness check asks the wrong engine about an index
        // it has never heard of and reports the service permanently unhealthy.
        _services.AddKeyedSingleton<ISearchIndexProvisioner>(
            SearchWellKnown.MeilisearchProviderName,
            (sp, _) => new MeilisearchIndexProvisioner(
                sp.GetRequiredService<global::Meilisearch.MeilisearchClient>(),
                sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
                indexDefinitions,
                rankingRules,
                sp.GetRequiredService<ILogger<MeilisearchIndexProvisioner>>()));

        _services.AddSingleton<ISearchIndexProvisioner>(sp =>
            sp.GetRequiredKeyedService<ISearchIndexProvisioner>(SearchWellKnown.MeilisearchProviderName));

        _services.AddKeyedSingleton<ISearchProviderDescriptor>(
            SearchWellKnown.MeilisearchProviderName,
            (sp, _) =>
            {
                var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
                return new MeilisearchProviderDescriptor(indexDefinitions, options.MaxTotalHits, options.MaxFacetValues);
            });

        _services.AddSingleton<ISearchProviderDescriptor>(sp =>
            sp.GetRequiredKeyedService<ISearchProviderDescriptor>(SearchWellKnown.MeilisearchProviderName));

        if (_tenantTokensRequested)
        {
            _services.AddScoped<ITenantSearchTokenIssuer>(sp => new MeilisearchTenantTokenIssuer(
                sp.GetRequiredService<global::Meilisearch.MeilisearchClient>(),
                sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<ILogger<MeilisearchTenantTokenIssuer>>()));
        }

        if (_rawClientAccessAllowed)
        {
            _services.AddSingleton<IMeilisearchRawClientAccessor>(sp =>
            {
                sp.GetRequiredService<ILogger<MeilisearchRawClientAccessor>>().MeilisearchRawClientAccessEnabled();
                return new MeilisearchRawClientAccessor(sp.GetRequiredService<global::Meilisearch.MeilisearchClient>());
            });
        }

        return _services;
    }
}
