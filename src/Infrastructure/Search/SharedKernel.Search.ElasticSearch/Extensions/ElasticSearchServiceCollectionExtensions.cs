using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Serialization;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;
using Result = SharedKernel.Primitives.Results.Result;

namespace SharedKernel.Search.ElasticSearch.Extensions;

/// <summary>The DI entry point for the ElasticSearch search provider.</summary>
public static class ElasticSearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ElasticSearch search provider, binding <see cref="ElasticSearchOptions"/> from
    /// <see cref="ElasticSearchOptions.SectionName"/> under <paramref name="configuration"/>.
    /// </summary>
    public static ElasticSearchBuilder AddSharedKernelElasticSearchSearch(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSharedKernelElasticSearchSearch(configuration.GetSection(ElasticSearchOptions.SectionName));
    }

    /// <summary>
    /// Registers the ElasticSearch search provider, binding <see cref="ElasticSearchOptions"/> from
    /// <paramref name="section"/> directly.
    /// </summary>
    public static ElasticSearchBuilder AddSharedKernelElasticSearchSearch(
        this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddValidatedOptions<ElasticSearchOptions>(section);

        var builder = new ElasticSearchBuilder(services);

        // ElasticsearchClient is a singleton — thread-safe and pools its own resources. Constructed
        // lazily on first resolution, by which point every .AddIndex(...)/.WithSourceSerializerContext(...)
        // call on `builder` has already run.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ElasticSearchOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<ElasticsearchClient>>();

            NodePool nodePool = options.Nodes.Length == 1
                ? new SingleNodePool(new Uri(options.Nodes[0]))
                : new StaticNodePool(options.Nodes.Select(node => new Uri(node)), randomize: false);

            ElasticsearchClientSettings settings;
            if (builder.SourceSerializerContext is { } context)
            {
                settings = new ElasticsearchClientSettings(
                    nodePool,
                    (_, clientSettings) => new DefaultSourceSerializer(clientSettings, context, _ => { }));
            }
            else
            {
                logger.ElasticSearchSourceSerializerContextMissing(builder.RegisteredIndexCount);
                settings = new ElasticsearchClientSettings(nodePool);
            }

            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                settings = settings.Authentication(new ApiKey(options.ApiKey));
            }
            else if (!string.IsNullOrEmpty(options.Username))
            {
                settings = settings.Authentication(new BasicAuthentication(options.Username, options.Password ?? string.Empty));
            }

            if (!string.IsNullOrEmpty(options.CertificateFingerprint))
            {
                settings = settings.CertificateFingerprint(options.CertificateFingerprint);
            }

            if (options.AllowInvalidCertificates)
            {
                logger.ElasticSearchCertificateValidationDisabled();
                settings = settings.ServerCertificateValidationCallback((_, _, _, _) => true);
            }

            settings = settings
                .RequestTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds))
                .PingTimeout(TimeSpan.FromSeconds(options.PingTimeoutSeconds));

            var client = new ElasticsearchClient(settings);

            logger.ElasticSearchClientConfigured(options.Nodes.Length, builder.RegisteredIndexCount);

            return client;
        });

        return builder;
    }

    /// <summary>
    /// Verifies that the connected cluster's version is one this client supports (9.x or 10.x),
    /// returning a failed <see cref="Result"/> naming the actual version when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asynchronous and explicitly invoked, because the alternative was worse than useless.</b> This
    /// check previously ran inside the <c>ElasticsearchClient</c> DI factory as
    /// <c>client.InfoAsync().GetAwaiter().GetResult()</c>, gated by a
    /// <c>ValidateEngineVersionOnStart</c> flag. That had three defects at once: the factory runs at
    /// <em>first resolution</em> of the client, not at startup — and because the index services are
    /// scoped, that is typically inside the first request, not during boot; it blocked a thread pool
    /// thread on a network round trip to do it; and it only logged, so an unsupported cluster started
    /// and served traffic anyway. An option named "validate on start" that neither runs on start nor
    /// validates is worse than no option, because it is believed.
    /// </para>
    /// <para>
    /// Call it from a startup task or a deployment smoke test, alongside
    /// <c>ISearchIndexProvisioner.VerifyRegisteredIndexesAsync</c>. An unreachable cluster is reported
    /// as <see cref="SearchErrors.Unreachable"/>, distinct from a reachable cluster running an
    /// unsupported version.
    /// </para>
    /// </remarks>
    public static async Task<Result> VerifyElasticSearchEngineVersionAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var client = services.GetRequiredService<ElasticsearchClient>();
        var options = services.GetRequiredService<IOptions<ElasticSearchOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<ElasticsearchClient>>();

        var info = await client.InfoAsync(cancellationToken).ConfigureAwait(false);
        if (!info.IsValidResponse)
        {
            logger.ElasticSearchEngineVersionUnsupported("unknown", SupportedVersionRange);
            return Result.Failure(SearchErrors.Unreachable(
                SearchWellKnown.ElasticSearchProviderName, string.Join(",", options.Nodes)));
        }

        if (!IsSupportedVersion(info.Version.Number))
        {
            logger.ElasticSearchEngineVersionUnsupported(info.Version.Number, SupportedVersionRange);
            return Result.Failure(SearchErrors.EngineVersionUnsupported(info.Version.Number, SupportedVersionRange));
        }

        logger.ElasticSearchEngineVersionVerified(info.Version.Number);
        return Result.Success();
    }

    private const string SupportedVersionRange = "9.x or 10.x";

    private static bool IsSupportedVersion(string version)
        => int.TryParse(version.Split('.')[0], out var major) && major is 9 or 10;
}
