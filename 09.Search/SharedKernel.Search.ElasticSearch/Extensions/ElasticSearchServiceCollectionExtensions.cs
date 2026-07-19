using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Serialization;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Search.ElasticSearch.Logging;
using SharedKernel.Search.ElasticSearch.Options;

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
                logger.ElasticSearchSourceSerializerContextMissing(typeof(object).Name);
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

            if (options.ValidateEngineVersionOnStart)
            {
                ValidateEngineVersion(client, logger);
            }

            return client;
        });

        return builder;
    }

    private static void ValidateEngineVersion(ElasticsearchClient client, ILogger logger)
    {
        try
        {
            // A synchronous, blocking startup check — the DI factory this runs inside is itself
            // synchronous, mirroring the "fail at composition time, not query time" philosophy for an
            // unsupported engine/client version pairing.
            var info = client.InfoAsync().GetAwaiter().GetResult();
            if (info.IsValidResponse && IsSupportedVersion(info.Version.Number))
            {
                logger.ElasticSearchEngineVersionVerified(info.Version.Number);
            }
            else
            {
                logger.ElasticSearchEngineVersionUnsupported(info.IsValidResponse ? info.Version.Number : "unknown", "9.x or 10.x");
            }
        }
        catch (Exception)
        {
            logger.ElasticSearchEngineVersionUnsupported("unknown", "9.x or 10.x");
        }
    }

    private static bool IsSupportedVersion(string version)
        => int.TryParse(version.Split('.')[0], out var major) && major is 9 or 10;
}
