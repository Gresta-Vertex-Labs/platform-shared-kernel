using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Util;
using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.Ollama;

namespace Shop.AppHost;

/// <summary>Every container of the platform. DEVELOPMENT-ONLY secrets: these are local stand-ins, never real systems.</summary>
public sealed class ShopInfrastructure
{
    public const string MinioUser = "shop-minio";
    public const string MinioPassword = "shop-minio-dev-secret";
    public const string MeilisearchKey = "shop-meili-dev-master-key-0123456789";
    public const string QdrantKey = "shop-qdrant-dev-key";
    public const string EmbeddingModel = "all-minilm";
    public const int EmbeddingDimension = 384;
    public const string ChatModel = "qwen2.5:0.5b";

    /// <summary>The buckets the platform's file stores use.</summary>
    public static readonly string[] Buckets = ["product-images"];

    public required IResourceBuilder<PostgresServerResource> Postgres { get; init; }
    public required IResourceBuilder<RedisResource> Redis { get; init; }
    public required IResourceBuilder<ContainerResource> Meilisearch { get; init; }
    public required IResourceBuilder<ContainerResource> Elasticsearch { get; init; }
    public required IResourceBuilder<ContainerResource> Qdrant { get; init; }
    public required IResourceBuilder<OllamaResource> Ollama { get; init; }
    public required IResourceBuilder<OllamaModelResource> EmbeddingModelResource { get; init; }
    public required IResourceBuilder<OllamaModelResource> ChatModelResource { get; init; }
    public required IResourceBuilder<ContainerResource> Minio { get; init; }
    public required IResourceBuilder<ContainerResource> Keycloak { get; init; }
    public required IResourceBuilder<RabbitMQServerResource> RabbitMq { get; init; }
    public required IResourceBuilder<ContainerResource> Temporal { get; init; }

    /// <summary>A connection string to one Shop database as one of the roles of postgres/01-roles.sql.</summary>
    public ReferenceExpression Database(string database, string role, string password)
    {
        var endpoint = Postgres.GetEndpoint("tcp");
        return ReferenceExpression.Create(
            $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Database={database};Username={role};Password={password}"
        );
    }

    public static ShopInfrastructure Add(IDistributedApplicationBuilder builder)
    {
        var postgres = builder
            .AddPostgres(ShopResources.Postgres)
            .WithImage("pgvector/pgvector", "pg16")
            .WithInitFiles("./postgres");

        var redis = builder.AddRedis(ShopResources.Redis).WithImageTag("7.4");

        var meilisearch = builder
            .AddContainer(ShopResources.Meilisearch, "getmeili/meilisearch", "v1.20.0")
            .WithEnvironment("MEILI_MASTER_KEY", MeilisearchKey)
            .WithEnvironment("MEILI_NO_ANALYTICS", "true")
            .WithHttpEndpoint(targetPort: 7700, name: "http")
            .WithHttpHealthCheck("/health", endpointName: "http");

        var elasticsearch = builder
            .AddContainer(
                ShopResources.Elasticsearch,
                "docker.elastic.co/elasticsearch/elasticsearch",
                "9.4.2"
            )
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithHttpEndpoint(targetPort: 9200, name: "http")
            .WithHttpHealthCheck("/_cluster/health", endpointName: "http");

        var qdrant = builder
            .AddContainer(ShopResources.Qdrant, "qdrant/qdrant", "v1.16.0")
            .WithEnvironment("QDRANT__SERVICE__API_KEY", QdrantKey)
            .WithHttpEndpoint(targetPort: 6333, name: "http")
            .WithEndpoint(targetPort: 6334, name: "grpc", scheme: "http")
            .WithHttpHealthCheck("/healthz", endpointName: "http");

        var ollama = builder.AddOllama(ShopResources.Ollama).WithDataVolume();
        var embeddingModel = ollama.AddModel("ollama-embedding", EmbeddingModel);
        var chatModel = ollama.AddModel("ollama-chat", ChatModel);

        var minio = builder
            .AddContainer(ShopResources.Minio, "pgsty/minio", "RELEASE.2026-08-04T00-00-00Z")
            .WithArgs("server", "/data")
            .WithEnvironment("MINIO_ROOT_USER", MinioUser)
            .WithEnvironment("MINIO_ROOT_PASSWORD", MinioPassword)
            .WithHttpEndpoint(targetPort: 9000, name: "http")
            .WithHttpHealthCheck("/minio/health/live", endpointName: "http");

        // MinIO starts with no buckets; create them before any service that waits for MinIO starts.
        builder.Eventing.Subscribe<ResourceReadyEvent>(
            minio.Resource,
            async (_, cancellationToken) =>
            {
                using var s3 = new AmazonS3Client(
                    new BasicAWSCredentials(MinioUser, MinioPassword),
                    new AmazonS3Config
                    {
                        ServiceURL = minio.GetEndpoint("http").Url,
                        ForcePathStyle = true,
                        AuthenticationRegion = "us-east-1",
                    }
                );
                foreach (string bucket in Buckets)
                {
                    if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket))
                    {
                        await s3.PutBucketAsync(bucket, cancellationToken);
                    }
                }
            }
        );

        // RabbitMQ with the delayed-message exchange plugin MassTransit's delayed delivery needs.
        var rabbitMq = builder
            .AddRabbitMQ(ShopResources.RabbitMq)
            .WithImage("masstransit/rabbitmq", "4.3.1");

        // The Temporal CLI's single-process development server (frontend, history, matching and an in-memory store).
        var temporal = builder
            .AddContainer(ShopResources.Temporal, "temporalio/temporal", "1.9.1")
            .WithArgs("server", "start-dev", "--ip", "0.0.0.0")
            .WithEndpoint(targetPort: 7233, name: "grpc", scheme: "http")
            .WithHttpEndpoint(targetPort: 8233, name: "ui")
            .WithHttpHealthCheck("/", endpointName: "ui");

        var keycloak = builder
            .AddContainer(ShopResources.Keycloak, "quay.io/keycloak/keycloak", "26.8.0")
            .WithArgs("start-dev", "--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithEnvironment("KC_HEALTH_ENABLED", "true")
            .WithBindMount("./keycloak", "/opt/keycloak/data/import", isReadOnly: true)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithHttpEndpoint(targetPort: 9000, name: "management")
            .WithHttpHealthCheck("/health/ready", endpointName: "management");

        return new ShopInfrastructure
        {
            Postgres = postgres,
            Redis = redis,
            Meilisearch = meilisearch,
            Elasticsearch = elasticsearch,
            Qdrant = qdrant,
            Ollama = ollama,
            EmbeddingModelResource = embeddingModel,
            ChatModelResource = chatModel,
            Minio = minio,
            Keycloak = keycloak,
            RabbitMq = rabbitMq,
            Temporal = temporal,
        };
    }
}
