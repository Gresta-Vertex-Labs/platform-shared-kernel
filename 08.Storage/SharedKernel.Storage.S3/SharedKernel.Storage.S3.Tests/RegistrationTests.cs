using System.Diagnostics;
using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.S3.Tests.Infrastructure;

namespace SharedKernel.Storage.S3.Tests;

[Collection(MinioCollection.Name)]
public sealed class RegistrationTests(MinioFixture minio)
{
    [Fact]
    public void The_s3_client_is_not_registered_as_IAmazonS3()
    {
        using ServiceProvider host = minio.CreateHost();

        host.GetService<IAmazonS3>().Should().BeNull();
    }

    [Fact]
    public void An_invalid_store_configuration_fails_with_every_problem_named()
    {
        using ServiceProvider host = minio.CreateHost(settings =>
        {
            settings["SharedKernel:Storage:Stores:files:Bucket"] = "Bad_Bucket";
            settings["SharedKernel:Storage:Stores:files:KeyPrefix"] = "no-slash";
            settings["SharedKernel:Storage:Stores:files:MaxPresignExpiry"] = "8.00:00:00";
        });

        Action resolve = () => host.GetRequiredKeyedService<IFileStorage>("files");

        resolve.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().HaveCount(3).And.AllSatisfy(f => f.Should().StartWith("Storage store 'files'"));
    }

    [Fact]
    public void Half_configured_static_credentials_are_refused()
    {
        using ServiceProvider host = minio.CreateHost(settings => settings.Remove("SharedKernel:Storage:S3:SecretAccessKey"));

        Action resolve = () => host.GetRequiredService<IOptions<S3StorageOptions>>().Value.ToString();

        resolve.Should().Throw<OptionsValidationException>().WithMessage("*AccessKeyId*SecretAccessKey*");
    }

    [Fact]
    public async Task A_healthy_bucket_passes_the_probe_and_a_missing_one_fails_it()
    {
        using ServiceProvider host = minio.CreateHost(extraStores: s3 => s3.AddStore("ghost", o => o.Bucket = "no-such-bucket"));
        IFileStorageHealthProbe probe = host.GetRequiredService<IFileStorageHealthProbe>();

        (await probe.ProbeAsync("files")).IsSuccess.Should().BeTrue();
        (await probe.ProbeAsync("ghost")).Error.Code.Should().Be(StorageErrorCodes.Unavailable);
    }

    [Fact]
    public async Task A_missing_bucket_is_a_provider_error_never_an_absent_object()
    {
        using ServiceProvider host = minio.CreateHost(extraStores: s3 => s3.AddStore("ghost", o => o.Bucket = "no-such-bucket"));
        IFileStorage ghost = host.GetRequiredKeyedService<IFileStorage>("ghost");

        // HEAD answers carry no error code, so ExistsAsync cannot tell a missing bucket from a missing object;
        // the readiness probe catches a misnamed bucket at startup.
        (await ghost.ExistsAsync("a")).Value.Should().BeFalse();
        (await ghost.DeleteAsync("a")).Error.Code.Should().Be(StorageErrorCodes.ProviderError);
        (await ghost.AbortMultipartUploadAsync(new MultipartUpload("a", "upload"))).Error.Code.Should().Be(StorageErrorCodes.ProviderError);
    }

    [Fact]
    public async Task An_unreachable_endpoint_returns_unavailable_instead_of_throwing()
    {
        using ServiceProvider host = minio.CreateHost(settings =>
        {
            settings["SharedKernel:Storage:S3:ServiceUrl"] = "http://127.0.0.1:1";
            settings["SharedKernel:Storage:S3:MaxRetries"] = "0";
            settings["SharedKernel:Storage:S3:RequestTimeout"] = "00:00:03";
        });
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");

        Result<bool> exists = await files.ExistsAsync("a");
        Result<FileReference> upload = await files.UploadAsync("a", new MemoryStream([1]));

        exists.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
        upload.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
    }

    [Fact]
    public async Task Cancellation_is_not_turned_into_an_error()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> download = () => files.DownloadAsync("a", cancellationToken: cancelled.Token);

        await download.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Stores_on_different_connections_copy_by_streaming()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(minio.Settings()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        IStorageBuilder storage = services.AddSharedKernelStorage();
        storage.AddS3(configuration).AddStore("files");
        storage.AddS3Compatible("Second", configuration, _ => (minio.CreateRawClient(), new S3Compatibility()))
            .AddStore("b-root");
        using ServiceProvider host = services.BuildServiceProvider();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        IFileStorage other = host.GetRequiredKeyedService<IFileStorage>("b-root");
        string key = TestData.UniqueKey();
        await files.UploadAsync(key, new MemoryStream([4, 2]), new FileUploadOptions { ContentType = "text/plain" });

        FileReference copy = (await files.CopyToAsync(key, other, key)).Ok();

        copy.Store.Should().Be("b-root");
        (await TestData.ReadAllAsync(other, key)).Should().Equal(4, 2);
        (await other.GetPropertiesAsync(key)).Ok().ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task Operations_emit_spans_and_never_record_object_keys()
    {
        using ServiceProvider host = minio.CreateHost();
        IFileStorage files = host.GetRequiredKeyedService<IFileStorage>("files");
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Storage",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        string key = TestData.UniqueKey("secret-name.txt");

        await files.UploadAsync(key, new MemoryStream([1]));
        await files.GetPropertiesAsync(TestData.UniqueKey());

        Activity upload = activities.Should().ContainSingle(a => a.DisplayName == "storage upload").Subject;
        upload.GetTagItem("storage.store").Should().Be("files");
        upload.GetTagItem("storage.provider").Should().Be("S3");
        Activity missing = activities.Should().ContainSingle(a => a.DisplayName == "storage get_properties").Subject;
        missing.GetTagItem("error.type").Should().Be(StorageErrorCodes.NotFound);
        activities.SelectMany(a => a.TagObjects).Select(t => t.Value?.ToString()).Should().NotContain(v => v != null && v.Contains("secret-name"));
    }
}

[Collection(MinioCollection.Name)]
public sealed class NamedConnectionTests(MinioFixture minio)
{
    [Fact]
    public async Task Named_connections_read_their_own_sections_and_serve_their_own_stores()
    {
        var settings = new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:Stores:public-assets:Bucket"] = MinioFixture.BucketA,
            ["SharedKernel:Storage:Stores:private-docs:Bucket"] = MinioFixture.BucketB,
        };
        foreach (string connection in new[] { "Public", "Private" })
        {
            settings[$"SharedKernel:Storage:S3:{connection}:ServiceUrl"] = minio.ServiceUrl;
            settings[$"SharedKernel:Storage:S3:{connection}:Region"] = "us-east-1";
            settings[$"SharedKernel:Storage:S3:{connection}:ForcePathStyle"] = "true";
            settings[$"SharedKernel:Storage:S3:{connection}:AccessKeyId"] = minio.AccessKey;
            settings[$"SharedKernel:Storage:S3:{connection}:SecretAccessKey"] = minio.SecretKey;
        }

        settings.Remove("SharedKernel:Storage:S3:Private:SecretAccessKey");
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        IStorageBuilder storage = services.AddSharedKernelStorage();
        storage.AddS3(configuration, "Public").AddStore("public-assets");
        storage.AddS3(configuration, "Private").AddTenantStore("private-docs");
        using ServiceProvider host = services.BuildServiceProvider();

        IFileStorage assets = host.GetRequiredKeyedService<IFileStorage>("public-assets");
        (await assets.UploadAsync(TestData.UniqueKey(), new MemoryStream([1]))).IsSuccess.Should().BeTrue();

        Action privateStore = () => host.GetRequiredKeyedService<ITenantFileStorage>("private-docs").ForTenant("t").ExistsAsync("a");
        privateStore.Should().Throw<OptionsValidationException>().WithMessage("*S3 connection 'Private'*SecretAccessKey*");
    }
}
