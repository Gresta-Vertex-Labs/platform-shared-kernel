using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using SharedKernel.AI.SemanticKernel.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.AI.SemanticKernel.Extensions;

/// <summary>The DI entry point for the Semantic Kernel LLM orchestration provider.</summary>
public static class SemanticKernelServiceCollectionExtensions
{
    private const string HttpClientName = "SharedKernel.AI.SemanticKernel";

    /// <summary>
    /// Registers the Semantic Kernel orchestration provider, binding <see cref="SemanticKernelOptions"/>
    /// from <see cref="SemanticKernelOptions.SectionName"/> under <paramref name="configuration"/>.
    /// </summary>
    public static SemanticKernelBuilder AddSharedKernelSemanticKernel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSharedKernelSemanticKernel(configuration.GetSection(SemanticKernelOptions.SectionName));
    }

    /// <summary>
    /// Registers the Semantic Kernel orchestration provider, binding <see cref="SemanticKernelOptions"/>
    /// from <paramref name="section"/> directly.
    /// </summary>
    public static SemanticKernelBuilder AddSharedKernelSemanticKernel(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddValidatedOptions<SemanticKernelOptions>(section);

        services.AddHttpClient(HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        // OpenAIClient is a singleton, built from the named IHttpClientFactory client — never
        // `new HttpClient()` (P-159/SK0013) — and shared by both the embedding generator and the chat
        // completion service so there is exactly one underlying connection/pipeline per process.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var clientOptions = new OpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(httpClient),
            };

            if (options.Endpoint is { } endpoint)
            {
                clientOptions.Endpoint = endpoint;
            }

            return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
        });

        return new SemanticKernelBuilder(services);
    }
}
