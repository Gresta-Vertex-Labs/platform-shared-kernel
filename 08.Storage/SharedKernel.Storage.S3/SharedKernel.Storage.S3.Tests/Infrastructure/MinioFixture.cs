using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;

namespace SharedKernel.Storage.S3.Tests.Infrastructure;

/// <summary>One MinIO container per test run, with two buckets, and a factory for fully registered storage hosts.</summary>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string BucketA = "store-a";
    public const string BucketB = "store-b";

    // A 2025 release: conditional writes (If-None-Match / If-Match) and flexible checksums need a recent MinIO.
    private readonly MinioContainer _container = new MinioBuilder("quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z").Build();

    public string ServiceUrl => _container.GetConnectionString();

    public string AccessKey => _container.GetAccessKey();

    public string SecretKey => _container.GetSecretKey();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using AmazonS3Client client = CreateRawClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketA });
        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketB });
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A plain SDK client, to inspect buckets independently of the code under test.</summary>
    public AmazonS3Client CreateRawClient() => new(
        new BasicAWSCredentials(AccessKey, SecretKey),
        new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

    /// <summary>Settings for the <c>S3</c> connection and the standard test stores.</summary>
    public Dictionary<string, string?> Settings() => new()
    {
        ["SharedKernel:Storage:S3:ServiceUrl"] = ServiceUrl,
        ["SharedKernel:Storage:S3:Region"] = "us-east-1",
        ["SharedKernel:Storage:S3:ForcePathStyle"] = "true",
        ["SharedKernel:Storage:S3:AccessKeyId"] = AccessKey,
        ["SharedKernel:Storage:S3:SecretAccessKey"] = SecretKey,
        ["SharedKernel:Storage:Stores:files:Bucket"] = BucketA,
        ["SharedKernel:Storage:Stores:files:MultipartPartSize"] = (5 * 1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["SharedKernel:Storage:Stores:prefixed:Bucket"] = BucketA,
        ["SharedKernel:Storage:Stores:prefixed:KeyPrefix"] = "pre/",
        ["SharedKernel:Storage:Stores:docs:Bucket"] = BucketB,
        ["SharedKernel:Storage:Stores:b-root:Bucket"] = BucketB,
        ["SharedKernel:Storage:Stores:archive:Bucket"] = BucketB,
        ["SharedKernel:Storage:Stores:archive:KeyPrefix"] = "archive/",
    };

    /// <summary>Builds a provider with the S3 connection and the standard stores: files, prefixed, archive, b-root (shared) and docs (tenant).</summary>
    public ServiceProvider CreateHost(Action<Dictionary<string, string?>>? adjust = null, Action<S3StorageBuilder>? extraStores = null)
    {
        Dictionary<string, string?> settings = Settings();
        adjust?.Invoke(settings);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        S3StorageBuilder s3 = services.AddSharedKernelStorage()
            .AddS3(configuration)
            .AddStore("files")
            .AddStore("prefixed")
            .AddStore("archive")
            .AddStore("b-root")
            .AddTenantStore("docs");
        extraStores?.Invoke(s3);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}

[CollectionDefinition(Name)]
public sealed class MinioCollection : ICollectionFixture<MinioFixture>
{
    public const string Name = "MinIO";
}
