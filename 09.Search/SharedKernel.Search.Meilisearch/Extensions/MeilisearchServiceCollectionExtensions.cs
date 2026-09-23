using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Search.Meilisearch.Logging;
using SharedKernel.Search.Meilisearch.Options;

namespace SharedKernel.Search.Meilisearch.Extensions;

/// <summary>The DI entry point for the Meilisearch search provider.</summary>
public static class MeilisearchServiceCollectionExtensions
{
    private const string HttpClientName = "SharedKernel.Search.Meilisearch";

    /// <summary>
    /// Registers the Meilisearch search provider, binding <see cref="MeilisearchOptions"/> from
    /// <see cref="MeilisearchOptions.SectionName"/> under <paramref name="configuration"/>.
    /// </summary>
    public static MeilisearchSearchBuilder AddSharedKernelMeilisearchSearch(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSharedKernelMeilisearchSearch(configuration.GetSection(MeilisearchOptions.SectionName));
    }

    /// <summary>
    /// Registers the Meilisearch search provider, binding <see cref="MeilisearchOptions"/> from
    /// <paramref name="section"/> directly.
    /// </summary>
    public static MeilisearchSearchBuilder AddSharedKernelMeilisearchSearch(
        this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddValidatedOptions<MeilisearchOptions>(section);

        services
            .AddHttpClient(HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
                client.BaseAddress = new Uri(options.Url);
                client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
            })
            // The MeilisearchClient below is a singleton and holds this HttpClient for the life of the
            // process, so IHttpClientFactory can never rotate its handler the way it does for a
            // short-lived client. PooledConnectionLifetime is what restores the property that rotation
            // normally provides: connections — and therefore the DNS resolution behind them — are
            // recycled on a schedule, so a Meilisearch pod rescheduled onto a new address is picked up
            // instead of being unreachable until the next deployment. SetHandlerLifetime would not help
            // here; nothing returns this client to the factory for the handler to expire.
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(
                    sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value.PooledConnectionLifetimeMinutes),
            });

        var builder = new MeilisearchSearchBuilder(services);

        // MeilisearchClient is a singleton, built from the named IHttpClientFactory client — never
        // `new HttpClient()`. Constructed lazily on first resolution, by which point every
        // .AddIndex(...) call on `builder` has already run, so RegisteredIndexCount is accurate.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var client = new global::Meilisearch.MeilisearchClient(httpClient, options.ApiKey);

            sp.GetRequiredService<ILogger<global::Meilisearch.MeilisearchClient>>()
                .MeilisearchClientConfigured(options.Url, builder.RegisteredIndexCount);

            return client;
        });

        return builder;
    }
}
