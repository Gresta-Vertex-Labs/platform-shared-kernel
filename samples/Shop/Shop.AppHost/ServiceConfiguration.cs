namespace Shop.AppHost;

/// <summary>
/// Maps the containers onto each service's configuration. Every key is the kernel's own configuration path, set through
/// environment variables, exactly as a deployment would.
/// </summary>
public static class ServiceConfiguration
{
    /// <summary>The 32-byte development key that seals entity versions (ETags). Never a production value.</summary>
    private const string DevEntityVersionKey = "c2hvcC1kZXYtZW50aXR5LXZlcnNpb24ta2V5LTAwMDE=";

    /// <summary>OIDC: Keycloak's realm as the issuer, http in development.</summary>
    public static IResourceBuilder<ProjectResource> WithIdentity(
        this IResourceBuilder<ProjectResource> service,
        ShopInfrastructure infra
    )
    {
        var keycloak = infra.Keycloak.GetEndpoint("http");
        return service
            .WithEnvironment(
                "SharedKernel__Security__Oidc__Authority",
                ReferenceExpression.Create($"{keycloak}/realms/{ShopResources.Identity.Realm}")
            )
            .WithEnvironment("SharedKernel__Security__Oidc__RequireHttpsMetadata", "false")
            .WaitFor(infra.Keycloak);
    }

    /// <summary>The shared Redis connection of 02.Caching (cache L2, backplane, locks, hashes, Pub/Sub).</summary>
    public static IResourceBuilder<ProjectResource> WithRedis(
        this IResourceBuilder<ProjectResource> service,
        ShopInfrastructure infra
    ) =>
        service
            .WithEnvironment(
                "SharedKernel__Caching__Redis__ConnectionString",
                infra.Redis.Resource.ConnectionStringExpression
            )
            .WaitFor(infra.Redis);

    /// <summary>A Shop database: the runtime role for the service, the migrator role for migrations.</summary>
    public static IResourceBuilder<ProjectResource> WithDatabase(
        this IResourceBuilder<ProjectResource> service,
        ShopInfrastructure infra,
        string database
    ) =>
        service
            .WithEnvironment(
                $"ConnectionStrings__{database}",
                infra.Database(database, "app_runtime", "runtime-dev")
            )
            .WithEnvironment(
                $"SharedKernel__Persistence__{database}__MigrationConnectionString",
                infra.Database(database, "app_migrator", "migrator-dev")
            )
            .WithEnvironment(
                $"SharedKernel__Persistence__{database}__RowLevelSecurity__CrossTenantConnectionString",
                infra.Database(database, "app_cross_tenant", "cross-tenant-dev")
            )
            .WaitFor(infra.Postgres);

    /// <summary>S3 on MinIO.</summary>
    public static IResourceBuilder<ProjectResource> WithObjectStorage(
        this IResourceBuilder<ProjectResource> service,
        ShopInfrastructure infra,
        params string[] stores
    )
    {
        service
            .WithEnvironment(
                "SharedKernel__Storage__S3__ServiceUrl",
                infra.Minio.GetEndpoint("http")
            )
            .WithEnvironment("SharedKernel__Storage__S3__Region", "us-east-1")
            .WithEnvironment("SharedKernel__Storage__S3__ForcePathStyle", "true")
            .WithEnvironment("SharedKernel__Storage__S3__AccessKeyId", ShopInfrastructure.MinioUser)
            .WithEnvironment(
                "SharedKernel__Storage__S3__SecretAccessKey",
                ShopInfrastructure.MinioPassword
            )
            .WaitFor(infra.Minio);
        foreach (string store in stores)
        {
            service.WithEnvironment($"SharedKernel__Storage__Stores__{store}__Bucket", store);
        }

        return service;
    }

    /// <summary>The Catalog service: every capability it uses.</summary>
    public static IResourceBuilder<ProjectResource> WithCatalogConfiguration(
        this IResourceBuilder<ProjectResource> service,
        ShopInfrastructure infra
    )
    {
        var qdrantGrpc = infra.Qdrant.GetEndpoint("grpc");
        var ollama = infra.Ollama.GetEndpoint("http");

        return service
            .WithIdentity(infra)
            .WithRedis(infra)
            .WithDatabase(infra, "catalog")
            .WithObjectStorage(infra, "product-images")
            .WithEnvironment("Catalog__Keys__KeyId", "dev-1")
            .WithEnvironment("Catalog__Keys__Material", DevEntityVersionKey)
            // 09.Search
            .WithEnvironment("Search__Meilisearch__Url", infra.Meilisearch.GetEndpoint("http"))
            .WithEnvironment("Search__Meilisearch__ApiKey", ShopInfrastructure.MeilisearchKey)
            .WithEnvironment(
                "Search__ElasticSearch__Nodes__0",
                infra.Elasticsearch.GetEndpoint("http")
            )
            .WithEnvironment("Search__ElasticSearch__NumberOfReplicas", "0")
            .WaitFor(infra.Meilisearch)
            .WaitFor(infra.Elasticsearch)
            // 10.Intelligence: Qdrant over gRPC, the models through Ollama's OpenAI-compatible endpoint
            .WithEnvironment(
                "Intelligence__Qdrant__Host",
                qdrantGrpc.Property(EndpointProperty.Host)
            )
            .WithEnvironment(
                "Intelligence__Qdrant__Port",
                qdrantGrpc.Property(EndpointProperty.Port)
            )
            .WithEnvironment("Intelligence__Qdrant__ApiKey", ShopInfrastructure.QdrantKey)
            .WithEnvironment(
                "Intelligence__SemanticKernel__Endpoint",
                ReferenceExpression.Create($"{ollama}/v1")
            )
            .WithEnvironment("Intelligence__SemanticKernel__ApiKey", "ollama")
            .WithEnvironment(
                "Intelligence__SemanticKernel__ChatModelId",
                ShopInfrastructure.ChatModel
            )
            .WithEnvironment(
                "Intelligence__SemanticKernel__EmbeddingModelId",
                ShopInfrastructure.EmbeddingModel
            )
            .WithEnvironment(
                "Intelligence__SemanticKernel__EmbeddingDimension",
                ShopInfrastructure.EmbeddingDimension.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
            .WithEnvironment("Catalog__Embeddings__Model", ShopInfrastructure.EmbeddingModel)
            .WithEnvironment(
                "Catalog__Embeddings__Dimension",
                ShopInfrastructure.EmbeddingDimension.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
            .WaitFor(infra.Qdrant)
            .WaitFor(infra.EmbeddingModelResource)
            .WaitFor(infra.ChatModelResource);
    }

    /// <summary>
    /// The Inventory service: Dapper over its database under row-level security, Redis, and gRPC over mutual TLS with
    /// the Shop's development PKI (Kestrel serves the PKI's <c>localhost</c> certificate; Ordering is allow-listed).
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithInventoryConfiguration(
        this IResourceBuilder<ProjectResource> service,
        ShopInfrastructure infra,
        ShopPki pki,
        string replica
    ) =>
        service
            .WithIdentity(infra)
            .WithRedis(infra)
            .WithDatabase(infra, "inventory")
            .WithEnvironment("Inventory__Replica", replica)
            .WithEnvironment("Kestrel__Certificates__Default__Path", pki.ServerCertificatePath)
            .WithEnvironment("Kestrel__Certificates__Default__Password", ShopPki.Password)
            .WithEnvironment("Inventory__Mtls__CaCertificatePath", pki.CaCertificatePath)
            .WithEnvironment(
                $"Inventory__Mtls__Clients__{pki.ClientThumbprint(ShopResources.Clients.Ordering)}",
                ShopResources.Clients.Ordering
            );
}
