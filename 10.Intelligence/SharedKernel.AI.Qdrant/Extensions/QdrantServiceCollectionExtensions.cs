using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using SharedKernel.AI.Qdrant.Logging;
using SharedKernel.AI.Qdrant.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.AI.Qdrant.Extensions;

/// <summary>The DI entry point for the Qdrant vector-database provider.</summary>
public static class QdrantServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Qdrant vector-database provider, binding <see cref="QdrantOptions"/> from
    /// <see cref="QdrantOptions.SectionName"/> under <paramref name="configuration"/>.
    /// </summary>
    public static QdrantBuilder AddSharedKernelQdrant(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSharedKernelQdrant(configuration.GetSection(QdrantOptions.SectionName));
    }

    /// <summary>
    /// Registers the Qdrant vector-database provider, binding <see cref="QdrantOptions"/> from
    /// <paramref name="section"/> directly.
    /// </summary>
    public static QdrantBuilder AddSharedKernelQdrant(this IServiceCollection services, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddValidatedOptions<QdrantOptions>(section);

        var builder = new QdrantBuilder(services);

        // Qdrant.Client's QdrantClient manages its own gRPC channel internally and exposes no
        // IHttpClientFactory-based construction overload — its public constructors accept only a
        // host/port (or Uri) + apiKey + timeout, never an HttpClient. This is a genuine SDK constraint,
        // not a P-159/SK0013 violation: the client is still registered exactly once as a SINGLETON,
        // which is the rule's actual intent (one pooled, reused connection, never a raw
        // per-call `new HttpClient()`); there is simply no exposed seam to route that singleton's
        // internal channel through the named-client factory the way an HTTP-based SDK would be.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var client = new QdrantClient(
                options.Host,
                options.Port,
                options.UseTls,
                options.ApiKey ?? string.Empty,
                TimeSpan.FromSeconds(options.GrpcTimeoutSeconds),
                loggerFactory!);

            sp.GetRequiredService<ILogger<QdrantClient>>()
                .QdrantClientConfigured(options.Host, options.Port, builder.RegisteredCollectionCount);

            return client;
        });

        // IQdrantClient is the sanctioned NSubstitute mocking seam for status-code-mapping tests and
        // the type QdrantVectorCollection<TRecord>/the Qdrant-exclusive accessors depend on; it resolves
        // to the SAME QdrantClient singleton registered above — never a second connection.
        services.AddSingleton<IQdrantClient>(sp => sp.GetRequiredService<QdrantClient>());

        return builder;
    }
}
