using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.S3.BlobUri;
using SharedKernel.Storage.S3.Extensions;
using SharedKernel.Storage.S3.FileStorage;

namespace SharedKernel.Storage.S3.Tests.Extensions;

/// <summary>
/// T-10: <c>AddSharedKernelS3Storage</c> DI registration tests — <see cref="IFileStorage"/>/
/// <see cref="IBlobUriGenerator"/> resolve, and <see cref="IAmazonS3"/> resolves as a singleton.
/// Uses <see cref="ServiceCollection"/> + <see cref="ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(IServiceCollection)"/>
/// only — no web host required for DI-level verification, per 08.Storage/CLAUDE.md's Test Rules.
/// </summary>
public sealed class S3StorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedKernelS3Storage_Resolves_IFileStorage_As_S3FileStorage()
    {
        var provider = BuildProviderWithValidConfig();

        var fileStorage = provider.GetRequiredService<IFileStorage>();

        fileStorage.Should().BeOfType<S3FileStorage>();
    }

    [Fact]
    public void AddSharedKernelS3Storage_Resolves_IBlobUriGenerator_As_S3BlobUriGenerator()
    {
        var provider = BuildProviderWithValidConfig();

        var blobUriGenerator = provider.GetRequiredService<IBlobUriGenerator>();

        blobUriGenerator.Should().BeOfType<S3BlobUriGenerator>();
    }

    [Fact]
    public void AddSharedKernelS3Storage_Resolves_IAmazonS3_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithValidConfig();

        var first = provider.GetRequiredService<IAmazonS3>();
        var second = provider.GetRequiredService<IAmazonS3>();

        first.Should().BeSameAs(second, "IAmazonS3 is thread-safe/connection-pooled and must be registered as a singleton");
    }

    [Fact]
    public void AddSharedKernelS3Storage_Resolves_IFileStorage_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithValidConfig();

        var first = provider.GetRequiredService<IFileStorage>();
        var second = provider.GetRequiredService<IFileStorage>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddSharedKernelS3Storage_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        var configuration = BuildValidConfig();

        var result = services.AddSharedKernelS3Storage(configuration);

        result.Should().BeSameAs(services);
    }

    private static IServiceProvider BuildProviderWithValidConfig()
    {
        var services = new ServiceCollection();
        var configuration = BuildValidConfig();

        // AddSharedKernelS3Storage() deliberately does not register ILogger<T> itself — that is the
        // consuming host's responsibility (e.g. via Host.CreateDefaultBuilder()'s built-in logging).
        // A minimal NullLogger<T> registration stands in for that host-level concern here.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSharedKernelS3Storage(configuration);

        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Storage:S3:ServiceUrl"] = "http://localhost:9000",
            ["SharedKernel:Storage:S3:AccessKeyId"] = "test-access-key",
            ["SharedKernel:Storage:S3:SecretAccessKey"] = "test-secret-key",
            ["SharedKernel:Storage:S3:ForcePathStyle"] = "true",
        })
        .Build();
}
