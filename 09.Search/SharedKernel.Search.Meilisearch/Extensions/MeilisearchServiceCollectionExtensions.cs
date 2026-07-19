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

        services.AddHttpClient(HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            client.BaseAddress = new Uri(options.Url);
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
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
