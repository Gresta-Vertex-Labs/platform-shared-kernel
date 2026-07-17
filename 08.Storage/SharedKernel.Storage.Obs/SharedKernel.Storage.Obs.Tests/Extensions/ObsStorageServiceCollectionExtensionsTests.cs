using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Obs.BlobUri;
using SharedKernel.Storage.Obs.Extensions;
using SharedKernel.Storage.Obs.FileStorage;

namespace SharedKernel.Storage.Obs.Tests.Extensions;

/// <summary>
/// T-16: <c>AddSharedKernelObsStorage</c> DI registration tests — mirrors
/// <c>SharedKernel.Storage.S3.Tests</c>' <c>S3StorageServiceCollectionExtensionsTests</c> (T-10)
/// one-for-one. <see cref="IFileStorage"/>/<see cref="IBlobUriGenerator"/> resolve, and
/// <see cref="IAmazonS3"/> resolves as a singleton. Uses <see cref="ServiceCollection"/> +
/// <c>BuildServiceProvider()</c> only — no web host required for DI-level verification.
/// </summary>
public sealed class ObsStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedKernelObsStorage_Resolves_IFileStorage_As_ObsFileStorage()
    {
        var provider = BuildProviderWithValidConfig();

        var fileStorage = provider.GetRequiredService<IFileStorage>();

        fileStorage.Should().BeOfType<ObsFileStorage>();
    }

    [Fact]
    public void AddSharedKernelObsStorage_Resolves_IBlobUriGenerator_As_ObsBlobUriGenerator()
    {
        var provider = BuildProviderWithValidConfig();

        var blobUriGenerator = provider.GetRequiredService<IBlobUriGenerator>();

        blobUriGenerator.Should().BeOfType<ObsBlobUriGenerator>();
    }

    [Fact]
    public void AddSharedKernelObsStorage_Resolves_IAmazonS3_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithValidConfig();

        var first = provider.GetRequiredService<IAmazonS3>();
        var second = provider.GetRequiredService<IAmazonS3>();

        first.Should().BeSameAs(second, "IAmazonS3 is thread-safe/connection-pooled and must be registered as a singleton");
    }

    [Fact]
    public void AddSharedKernelObsStorage_Resolves_IFileStorage_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithValidConfig();

        var first = provider.GetRequiredService<IFileStorage>();
        var second = provider.GetRequiredService<IFileStorage>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddSharedKernelObsStorage_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        var configuration = BuildValidConfig();

        var result = services.AddSharedKernelObsStorage(configuration);

        result.Should().BeSameAs(services);
    }

    private static IServiceProvider BuildProviderWithValidConfig()
    {
        var services = new ServiceCollection();
        var configuration = BuildValidConfig();

        // AddSharedKernelObsStorage() deliberately does not register ILogger<T> itself — that is the
        // consuming host's responsibility (e.g. via Host.CreateDefaultBuilder()'s built-in logging).
        // A minimal NullLogger<T> registration stands in for that host-level concern here.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSharedKernelObsStorage(configuration);

        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:Obs:Endpoint"] = "https://obs.ap-southeast-1.myhuaweicloud.com",
            ["SharedKernel:Storage:Obs:AccessKeyId"] = "test-access-key",
            ["SharedKernel:Storage:Obs:SecretAccessKey"] = "test-secret-key",
            ["SharedKernel:Storage:Obs:ForcePathStyle"] = "true",
        })
        .Build();
}
