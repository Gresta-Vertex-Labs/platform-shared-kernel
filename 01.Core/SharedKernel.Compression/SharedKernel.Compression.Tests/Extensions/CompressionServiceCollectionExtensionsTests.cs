using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Extensions;
using Xunit;

namespace SharedKernel.Compression.Tests.Extensions;

public sealed class CompressionServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddSharedKernelCompression_RegistersBrotliAsUnkeyedDefault()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCompression(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<BrotliPayloadCompressor>(provider.GetRequiredService<IPayloadCompressor>());
    }

    [Fact]
    public void AddSharedKernelCompression_RegistersBrotliAsKeyedSingleton()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCompression(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<BrotliPayloadCompressor>(
            provider.GetRequiredKeyedService<IPayloadCompressor>(
                CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey));
    }

    [Fact]
    public void AddSharedKernelCompression_RegistersGZipAsKeyedSingletonOnly()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCompression(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<GZipPayloadCompressor>(
            provider.GetRequiredKeyedService<IPayloadCompressor>(
                CompressionServiceCollectionExtensions.GZipPayloadCompressorKey));
        // The unkeyed default must resolve to Brotli, never GZip.
        Assert.IsType<BrotliPayloadCompressor>(provider.GetRequiredService<IPayloadCompressor>());
    }

    [Fact]
    public async Task InvalidCompressionOptions_ThrowsAtHostStartup()
    {
        // "99" binds successfully as a raw underlying int value (System.IO.Compression.CompressionLevel
        // has no [Flags]/defined member 99) — only the [EnumDataType] DataAnnotations check on
        // CompressionOptions.Level catches this at ValidateOnStart(), mirroring how CryptographyOptions'
        // invalid-config test exercises a [Range] violation rather than a configuration-binding failure.
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Compression:Level"] = "99",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelCompression(invalidConfig))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void NullServices_Throws()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddSharedKernelCompression(EmptyConfiguration()));
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddSharedKernelCompression(null!));
    }
}
