using System.Globalization;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DocumentsApi;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Minio;

namespace DocumentsApi.Tests.Infrastructure;

/// <summary>
/// The backends every scenario runs against: MinIO always, and the real clouds when <c>SK_LIVE_*</c> is set; plus
/// Gotenberg for HTML-to-PDF.
/// Each run writes only under <c>sharedkernel-samples/{run id}/</c> and deletes what it wrote.
/// </summary>
public sealed class Backends : IAsyncLifetime
{
    public const string MinIO = "minio";
    public const string Live = "live";

    private const string MinioImage = "pgsty/minio:RELEASE.2026-08-04T00-00-00Z";
    private static readonly string[] MinioBuckets = ["sample-assets", "sample-documents", "sample-archive"];

    private const string GotenbergImage = "gotenberg/gotenberg:8.37.0";
    private const int GotenbergPort = 3000;
    private const string GotenbergBaseUrl = "SharedKernel:Reporting:Gotenberg:BaseUrl";

    private readonly MinioContainer _minio = new MinioBuilder(MinioImage).Build();

    // HTML-to-PDF for the report scenarios, and the "gotenberg" readiness probe.
    private readonly IContainer _gotenberg = new ContainerBuilder(GotenbergImage)
        .WithPortBinding(GotenbergPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(GotenbergPort).ForPath("/health")))
        .Build();

    private readonly Dictionary<string, SampleHost> _hosts = [];

    public static string RunId { get; } =
        $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>Gets whether every live credential variable is set.</summary>
    public static bool LiveAvailable => LiveSettings() is not null;

    public SampleHost this[string backend] => _hosts[backend];

    /// <summary>Every (backend, store) pair to run a scenario against.</summary>
    public static TheoryData<string, string> AllStores() => Cases(Stores.Assets, Stores.Documents, Stores.Archive);

    /// <summary>Every (backend, store) pair for shared stores only.</summary>
    public static TheoryData<string, string> SharedStores() => Cases(Stores.Assets, Stores.Archive);

    public static TheoryData<string> AllBackends()
    {
        var data = new TheoryData<string> { MinIO };
        if (LiveAvailable)
        {
            data.Add(Live);
        }

        return data;
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_minio.StartAsync(), _gotenberg.StartAsync());
        string gotenbergUrl = $"http://{_gotenberg.Hostname}:{_gotenberg.GetMappedPublicPort(GotenbergPort)}";
        using (var client = new AmazonS3Client(
            new BasicAWSCredentials(_minio.GetAccessKey(), _minio.GetSecretKey()),
            new AmazonS3Config { ServiceURL = _minio.GetConnectionString(), ForcePathStyle = true, AuthenticationRegion = "us-east-1" }))
        {
            foreach (string bucket in MinioBuckets)
            {
                await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
            }
        }

        Dictionary<string, string?> minio = MinioSettings();
        minio[GotenbergBaseUrl] = gotenbergUrl;
        _hosts[MinIO] = new SampleHost(MinIO, minio);
        if (LiveSettings() is { } live)
        {
            live[GotenbergBaseUrl] = gotenbergUrl;
            _hosts[Live] = new SampleHost(Live, live);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (SampleHost host in _hosts.Values)
        {
            await host.CleanUpAsync();
            await host.DisposeAsync();
        }

        await _minio.DisposeAsync();
        await _gotenberg.DisposeAsync();
    }

    private static TheoryData<string, string> Cases(params string[] stores)
    {
        var data = new TheoryData<string, string>();
        foreach (string backend in AllBackends())
        {
            foreach (string store in stores)
            {
                data.Add(backend, store);
            }
        }

        return data;
    }

    private Dictionary<string, string?> MinioSettings()
    {
        var settings = StoreSettings(MinioBuckets[0], MinioBuckets[1], MinioBuckets[2]);
        foreach (string connection in new[] { "Public", "Private" })
        {
            string section = $"SharedKernel:Storage:S3:{connection}";
            settings[$"{section}:ServiceUrl"] = _minio.GetConnectionString();
            settings[$"{section}:Region"] = "us-east-1";
            settings[$"{section}:ForcePathStyle"] = "true";
            settings[$"{section}:AccessKeyId"] = _minio.GetAccessKey();
            settings[$"{section}:SecretAccessKey"] = _minio.GetSecretKey();
        }

        // MinIO also stands in for OBS: same S3 API, with the OBS compatibility profile.
        settings["SharedKernel:Storage:Obs:Endpoint"] = _minio.GetConnectionString();
        settings["SharedKernel:Storage:Obs:Region"] = "us-east-1";
        settings["SharedKernel:Storage:Obs:ForcePathStyle"] = "true";
        settings["SharedKernel:Storage:Obs:AccessKeyId"] = _minio.GetAccessKey();
        settings["SharedKernel:Storage:Obs:SecretAccessKey"] = _minio.GetSecretKey();

        // MinIO without a KMS rejects SSE headers; encryption is exercised against the real clouds.
        settings["SharedKernel:Storage:Stores:documents:Encryption"] = "BucketDefault";
        return settings;
    }

    /// <summary>Settings for the real clouds, or <see langword="null"/> when a variable is missing.</summary>
    private static Dictionary<string, string?>? LiveSettings()
    {
        string?[] values =
        [
            Env("SK_LIVE_S3_REGION"),
            Env("SK_LIVE_S3_PUBLIC_BUCKET"), Env("SK_LIVE_S3_PUBLIC_ACCESS_KEY"), Env("SK_LIVE_S3_PUBLIC_SECRET_KEY"),
            Env("SK_LIVE_S3_PRIVATE_BUCKET"), Env("SK_LIVE_S3_PRIVATE_ACCESS_KEY"), Env("SK_LIVE_S3_PRIVATE_SECRET_KEY"),
            Env("SK_LIVE_OBS_ENDPOINT"), Env("SK_LIVE_OBS_BUCKET"), Env("SK_LIVE_OBS_ACCESS_KEY"), Env("SK_LIVE_OBS_SECRET_KEY"),
        ];
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        var settings = StoreSettings(values[1]!, values[4]!, values[8]!);
        settings["SharedKernel:Storage:S3:Public:Region"] = values[0];
        settings["SharedKernel:Storage:S3:Public:AccessKeyId"] = values[2];
        settings["SharedKernel:Storage:S3:Public:SecretAccessKey"] = values[3];
        settings["SharedKernel:Storage:S3:Private:Region"] = values[0];
        settings["SharedKernel:Storage:S3:Private:AccessKeyId"] = values[5];
        settings["SharedKernel:Storage:S3:Private:SecretAccessKey"] = values[6];
        settings["SharedKernel:Storage:Obs:Endpoint"] = values[7];
        settings["SharedKernel:Storage:Obs:AccessKeyId"] = values[9];
        settings["SharedKernel:Storage:Obs:SecretAccessKey"] = values[10];
        settings["SharedKernel:Storage:Stores:archive:Encryption"] = "S3Managed";
        return settings;
    }

    private static Dictionary<string, string?> StoreSettings(string assetsBucket, string documentsBucket, string archiveBucket) => new()
    {
        ["SharedKernel:Storage:Stores:assets:Bucket"] = assetsBucket,
        ["SharedKernel:Storage:Stores:assets:KeyPrefix"] = $"sharedkernel-samples/{RunId}/assets/",
        ["SharedKernel:Storage:Stores:documents:Bucket"] = documentsBucket,
        ["SharedKernel:Storage:Stores:documents:KeyPrefix"] = $"sharedkernel-samples/{RunId}/documents/",
        ["SharedKernel:Storage:Stores:archive:Bucket"] = archiveBucket,
        ["SharedKernel:Storage:Stores:archive:KeyPrefix"] = $"sharedkernel-samples/{RunId}/archive/",
        ["SharedKernel:Storage:Stores:assets:MultipartPartSize"] = (5 * 1024 * 1024).ToString(CultureInfo.InvariantCulture),
        ["SharedKernel:Storage:Stores:archive:MultipartPartSize"] = (5 * 1024 * 1024).ToString(CultureInfo.InvariantCulture),
        ["SharedKernel:Storage:Stores:documents:MultipartPartSize"] = (5 * 1024 * 1024).ToString(CultureInfo.InvariantCulture),
    };

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
}

[CollectionDefinition(Name)]
public sealed class BackendsCollection : ICollectionFixture<Backends>
{
    public const string Name = "Storage backends";
}
