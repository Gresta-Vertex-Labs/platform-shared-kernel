using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
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
        _services.AddSingleton<ISearchIndexProvisioner>(sp => new MeilisearchIndexProvisioner(
            sp.GetRequiredService<global::Meilisearch.MeilisearchClient>(),
            sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value,
            sp.GetRequiredService<ILogger<MeilisearchIndexProvisioner>>()));

        _services.AddSingleton<ISearchProviderDescriptor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            return new MeilisearchProviderDescriptor(indexDefinitions, options.MaxTotalHits, options.MaxFacetValues);
        });

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
