using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.AI.Qdrant.Extensions;
using SharedKernel.AI.SemanticKernel.Extensions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Storage;
using SharedKernel.Validation.FluentValidation;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Application.Search;
using Shop.Catalog.Infrastructure.Persistence;
using Shop.Catalog.Infrastructure.Pricing;
using Shop.Catalog.Infrastructure.Search;

namespace Shop.Catalog.Infrastructure;

/// <summary>Root key material of the catalog (<c>Catalog:Keys</c>): seals the ETag of every product version.</summary>
public sealed class CatalogKeyOptions
{
    public const string SectionName = "Catalog:Keys";

    /// <summary>The current key id.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The 32-byte key, base64. Development value from the AppHost; a secret store in production.</summary>
    public string Material { get; set; } = string.Empty;
}

/// <summary>Registers every adapter behind the catalog's ports.</summary>
public static class CatalogInfrastructure
{
    /// <summary>
    /// PostgreSQL (EF Core, multi-tenant with row-level security), the two-level cache over Redis, Redis Pub/Sub,
    /// Meilisearch and Elasticsearch, Qdrant and the OpenAI-compatible model endpoint, and the S3 image store.
    /// </summary>
    public static IHostApplicationBuilder AddCatalogInfrastructure(
        this IHostApplicationBuilder builder
    )
    {
        IConfiguration configuration = builder.Configuration;
        IServiceCollection services = builder.Services;

        services.AddClock();

        // The entity-version (ETag) root key. 06.Persistence derives its own subkey from it.
        services
            .AddOptions<CatalogKeyOptions>()
            .Bind(configuration.GetSection(CatalogKeyOptions.SectionName))
            .Validate(
                o => o.KeyId.Length > 0 && o.Material.Length > 0,
                "Catalog:Keys needs KeyId and Material."
            )
            .ValidateOnStart();
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp =>
        {
            var keys = sp.GetRequiredService<IOptions<CatalogKeyOptions>>().Value;
            return new StaticEncryptionKeyProvider(
                keys.KeyId,
                [new CryptographicKey(keys.KeyId, Convert.FromBase64String(keys.Material))]
            );
        });

        // 06.Persistence: one call; migrations run as the migrator role, one replica at a time.
        builder.AddSharedKernelPostgres<CatalogDbContext>(
            CatalogDatabase.ConnectionName,
            p => CatalogDatabase.Configure(p).UseServiceName("catalog-api").MigrateOnStartup()
        );

        // 02.Caching: FusionCache L1 + Redis L2 and backplane, and Redis Pub/Sub, all on one shared connection.
        services.AddRedisConnection(configuration);
        services.AddSharedKernelCaching(configuration).AddRedisL2(configuration);
        services.AddRedisChannelService();
        services.AddSingleton<IPriceChangeBroadcaster, RedisPriceChangeBroadcaster>();
        services.AddSingleton<PriceChangeLog>();
        services.AddHostedService<PriceChangeListener>();

        // 09.Search: the storefront on Meilisearch, the back office on Elasticsearch (different document types).
        services
            .AddSharedKernelMeilisearchSearch(configuration)
            .AddIndex<ProductDocument>(CatalogIndexes.Storefront, CatalogDefinitions.Storefront)
            .Build();
        services
            .AddSharedKernelElasticSearchSearch(configuration)
            .AddIndex<ProductAdminDocument>(
                CatalogIndexes.BackOffice,
                CatalogIndexes.BackOffice,
                CatalogDefinitions.BackOffice
            )
            .Build();

        // 10.Intelligence: embeddings and chat through the OpenAI-compatible endpoint, vectors in Qdrant.
        services
            .AddOptions<CatalogEmbeddingOptions>()
            .Bind(configuration.GetSection(CatalogEmbeddingOptions.SectionName))
            .Validate(
                o => o.Model.Length > 0 && o.Dimension > 0,
                "Catalog:Embeddings needs Model and Dimension."
            )
            .ValidateOnStart();
        var embedding =
            configuration
                .GetSection(CatalogEmbeddingOptions.SectionName)
                .Get<CatalogEmbeddingOptions>()
            ?? new();
        services.AddSharedKernelSemanticKernel(configuration).Build();
        services
            .AddSharedKernelQdrant(configuration)
            .AddCollection<ProductVector>(
                CatalogIndexes.Vectors,
                CatalogDefinitions.Vectors(embedding.Model, embedding.Dimension)
            )
            .Build();
        services.AddHostedService<CatalogProvisioning>();

        // 08.Storage: product images, one key prefix per tenant.
        services
            .AddSharedKernelStorage()
            .AddS3(configuration)
            .AddTenantStore(CatalogIndexes.ImageStore);

        // The application's helpers and the FluentValidation bridge for its validators.
        services.AddScoped<ProductIndexer>();
        services.AddFluentValidationRequestValidators(typeof(CreateProductCommand).Assembly);

        return builder;
    }
}
