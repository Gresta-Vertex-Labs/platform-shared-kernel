using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Obs;
using Testcontainers.Minio;

namespace SharedKernel.Storage.Obs.Tests;

/// <summary>MinIO stands in for OBS's S3-compatible endpoint; OBS itself is not reachable from CI.</summary>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string Bucket = "obs-bucket";

    private readonly MinioContainer _container = new MinioBuilder("quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z").Build();

    public string ServiceUrl => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(_container.GetAccessKey(), _container.GetSecretKey()),
            new AmazonS3Config { ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket });
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public Dictionary<string, string?> Settings() => new()
    {
        ["SharedKernel:Storage:Obs:Endpoint"] = ServiceUrl,
        ["SharedKernel:Storage:Obs:Region"] = "us-east-1",
        ["SharedKernel:Storage:Obs:ForcePathStyle"] = "true",
        ["SharedKernel:Storage:Obs:AccessKeyId"] = _container.GetAccessKey(),
        ["SharedKernel:Storage:Obs:SecretAccessKey"] = _container.GetSecretKey(),
        ["SharedKernel:Storage:Stores:archive:Bucket"] = Bucket,
        ["SharedKernel:Storage:Stores:tenant-archive:Bucket"] = Bucket,
    };

    public ServiceProvider CreateHost(Action<Dictionary<string, string?>>? adjust = null)
    {
        Dictionary<string, string?> settings = Settings();
        adjust?.Invoke(settings);
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelStorage().AddObs(configuration).AddStore("archive").AddTenantStore("tenant-archive");
        return services.BuildServiceProvider();
    }
}

public sealed class ObsStorageTests(MinioFixture minio) : IClassFixture<MinioFixture>
{
    [Fact]
    public async Task Objects_round_trip_through_the_obs_connection()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage archive = host.GetRequiredKeyedService<IFileStorage>("archive");
        string key = $"{Guid.NewGuid():N}/a.txt";

        FileReference reference = (await archive.UploadAsync(key, new MemoryStream([1, 2]), new FileUploadOptions { ContentType = "text/plain" })).Value;
        await using FileDownload download = (await archive.DownloadAsync(key)).Value;
        using var buffer = new MemoryStream();
        await download.Content.CopyToAsync(buffer);

        reference.Store.Should().Be("archive");
        buffer.ToArray().Should().Equal(1, 2);
        download.Properties.ContentType.Should().Be("text/plain");
        (await host.GetRequiredService<IFileStorageHealthProbe>().ProbeAsync("archive")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Full_downloads_of_empty_and_non_empty_objects_work_without_the_etag_md5_check()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage archive = host.GetRequiredKeyedService<IFileStorage>("archive");
        string empty = $"{Guid.NewGuid():N}/empty.txt";
        string full = $"{Guid.NewGuid():N}/full.txt";
        await archive.UploadAsync(empty, new MemoryStream([]), new FileUploadOptions { Tags = new Dictionary<string, string> { ["k"] = "v" } });
        await archive.UploadAsync(full, new MemoryStream([1, 2, 3]));

        await using FileDownload emptyDownload = (await archive.DownloadAsync(empty)).Value;
        await using FileDownload fullDownload = (await archive.DownloadAsync(full)).Value;
        using var buffer = new MemoryStream();
        await fullDownload.Content.CopyToAsync(buffer);

        emptyDownload.Length.Should().Be(0);
        buffer.ToArray().Should().Equal(1, 2, 3);
        fullDownload.Range.Should().BeNull();
        fullDownload.Properties.ContentLength.Should().Be(3);
        (await archive.DownloadAsync($"{Guid.NewGuid():N}/missing")).Error.Code.Should().Be(StorageErrorCodes.NotFound);
    }

    [Fact]
    public async Task Tenant_stores_work_on_obs()
    {
        using ServiceProvider host = minio.CreateHost();
        ITenantFileStorage tenants = host.GetRequiredKeyedService<ITenantFileStorage>("tenant-archive");

        await tenants.ForTenant("a").UploadAsync("x.txt", new MemoryStream([1]));

        (await tenants.ForTenant("a").ExistsAsync("x.txt")).Value.Should().BeTrue();
        (await tenants.ForTenant("b").ExistsAsync("x.txt")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task Features_obs_lacks_fail_as_not_supported_before_any_request()
    {
        using ServiceProvider host = minio.CreateHost(s => s["SharedKernel:Storage:Obs:Endpoint"] = "http://127.0.0.1:1");
        IFileStorage archive = host.GetRequiredKeyedService<IFileStorage>("archive");
        byte[] content = [1];

        Result<FileReference>[] results =
        [
            await archive.UploadAsync("a", new MemoryStream(content), new FileUploadOptions { Condition = WriteCondition.IfNotExists }),
            await archive.UploadAsync("a", new MemoryStream(content), new FileUploadOptions { ChecksumSha256 = Convert.ToBase64String(new byte[32]) }),
            await archive.CopyAsync("a", "b", new FileCopyOptions { Condition = WriteCondition.IfNotExists }),
        ];
        Result<PresignedRequest> createOnlyUrl = await archive.CreateUploadUrlAsync("a", new PresignedUploadOptions
        {
            Expiry = TimeSpan.FromMinutes(1),
            ContentType = "text/plain",
            CreateOnly = true,
        });

        results.Should().AllSatisfy(r => r.Error.Code.Should().Be(StorageErrorCodes.NotSupported));
        createOnlyUrl.Error.Code.Should().Be(StorageErrorCodes.NotSupported);
    }

    [Theory]
    [InlineData("https://obs.tr-west-1.myhuaweicloud.com", null, "tr-west-1")]
    [InlineData("https://obs.ap-southeast-3.myhuaweicloud.com", "cn-north-4", "cn-north-4")]
    [InlineData("https://storage.example.internal", null, null)]
    public void The_signing_region_comes_from_the_endpoint_unless_configured(string endpoint, string? region, string? expected) =>
        ObsStorageOptionsValidator.ResolveRegion(new ObsStorageOptions { Endpoint = endpoint, Region = region }).Should().Be(expected);

    [Fact]
    public void Missing_credentials_and_an_unknown_region_are_reported_at_startup()
    {
        using ServiceProvider host = minio.CreateHost(s =>
        {
            s["SharedKernel:Storage:Obs:Endpoint"] = "https://storage.example.internal";
            s.Remove("SharedKernel:Storage:Obs:Region");
            s.Remove("SharedKernel:Storage:Obs:SecretAccessKey");
        });

        Action resolve = () => _ = host.GetRequiredService<IOptions<ObsStorageOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>().Which.Failures.Should().HaveCount(2);
    }
}
