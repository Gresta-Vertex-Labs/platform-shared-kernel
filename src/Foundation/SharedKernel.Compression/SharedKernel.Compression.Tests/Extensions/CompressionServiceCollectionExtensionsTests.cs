using System.Buffers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Extensions;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests.Extensions;

public sealed class CompressionServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IServiceProvider Registered(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCompression(configuration ?? EmptyConfiguration());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void RegistersFramedBrotliAsTheUnkeyedDefault()
    {
        var compressor = Registered().GetRequiredService<IPayloadCompressor>();

        Assert.IsType<BrotliPayloadCompressor>(compressor);
        Assert.Equal(CompressionAlgorithm.Brotli, compressor.Algorithm);
        Assert.Equal(CompressionFraming.Framed, compressor.Framing);
    }

    [Theory]
    [InlineData(CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey, CompressionAlgorithm.Brotli, CompressionFraming.Framed)]
    [InlineData(CompressionServiceCollectionExtensions.GZipPayloadCompressorKey, CompressionAlgorithm.GZip, CompressionFraming.Framed)]
    [InlineData(CompressionServiceCollectionExtensions.RawBrotliPayloadCompressorKey, CompressionAlgorithm.Brotli, CompressionFraming.Raw)]
    [InlineData(CompressionServiceCollectionExtensions.RawGZipPayloadCompressorKey, CompressionAlgorithm.GZip, CompressionFraming.Raw)]
    public void EachKey_ResolvesTheCompressorItNames(
        string key,
        CompressionAlgorithm algorithm,
        CompressionFraming framing)
    {
        var compressor = Registered().GetRequiredKeyedService<IPayloadCompressor>(key);

        Assert.Equal(algorithm, compressor.Algorithm);
        Assert.Equal(framing, compressor.Framing);
    }

    [Fact]
    public void GZip_HasNoUnkeyedRegistration()
    {
        var provider = Registered();

        Assert.IsType<BrotliPayloadCompressor>(provider.GetRequiredService<IPayloadCompressor>());
        Assert.Single(provider.GetServices<IPayloadCompressor>());
    }

    [Fact]
    public void EveryRegistration_IsASingleton()
    {
        var provider = Registered();

        Assert.Same(
            provider.GetRequiredService<IPayloadCompressor>(),
            provider.GetRequiredService<IPayloadCompressor>());
        Assert.Same(
            provider.GetRequiredKeyedService<IPayloadCompressor>(CompressionServiceCollectionExtensions.GZipPayloadCompressorKey),
            provider.GetRequiredKeyedService<IPayloadCompressor>(CompressionServiceCollectionExtensions.GZipPayloadCompressorKey));
    }

    [Fact]
    public void CalledTwice_RegistersEachServiceExactlyOnce()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelCompression(EmptyConfiguration());
        services.AddSharedKernelCompression(EmptyConfiguration());

        Assert.Single(services.Where(d => d.ServiceType == typeof(IPayloadCompressor) && d.ServiceKey is null));
        Assert.Single(services.Where(d =>
            d.ServiceType == typeof(IPayloadCompressor)
            && Equals(d.ServiceKey, CompressionServiceCollectionExtensions.GZipPayloadCompressorKey)));
        Assert.Single(services.BuildServiceProvider().GetServices<IPayloadCompressor>());
    }

    [Fact]
    public void ConsumerRegistrationMadeFirst_WinsOverThePlatformDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPayloadCompressor, FakeCompressor>();

        services.AddSharedKernelCompression(EmptyConfiguration());

        Assert.IsType<FakeCompressor>(services.BuildServiceProvider().GetRequiredService<IPayloadCompressor>());
    }

    [Fact]
    public void BindsOptionsFromTheSectionTheOptionsTypeDeclares()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Compression:Level"] = "Fastest",
                ["SharedKernel:Compression:MaxDecompressedSize"] = "12345",
            })
            .Build();

        var options = Registered(configuration).GetRequiredService<IOptions<CompressionOptions>>().Value;

        Assert.Equal(System.IO.Compression.CompressionLevel.Fastest, options.Level);
        Assert.Equal(12345, options.MaxDecompressedSize);
        Assert.Equal("SharedKernel:Compression", CompressionOptions.SectionName);
    }

    [Fact]
    public void DefaultOptions_ApplyWhenNoSectionIsPresent()
    {
        var options = Registered().GetRequiredService<IOptions<CompressionOptions>>().Value;

        Assert.Equal(System.IO.Compression.CompressionLevel.Optimal, options.Level);
        Assert.Equal(CompressionOptions.DefaultMaxDecompressedSize, options.MaxDecompressedSize);
    }

    [Fact]
    public async Task AnUndefinedCompressionLevel_ThrowsAtHostStartup()
    {
        // "99" binds successfully as a raw underlying int; only the [EnumDataType] check catches it,
        // and only because ValidateOnStart() runs it before the first request rather than on first use.
        await AssertStartupRejects(new Dictionary<string, string?>
        {
            ["SharedKernel:Compression:Level"] = "99",
        });
    }

    [Fact]
    public async Task ANonPositiveMaxDecompressedSize_ThrowsAtHostStartup()
    {
        // Zero or negative would disable the bomb guard by making every payload "too large", or worse,
        // be read as unbounded — rejected at startup instead.
        await AssertStartupRejects(new Dictionary<string, string?>
        {
            ["SharedKernel:Compression:MaxDecompressedSize"] = "0",
        });
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

    private static async Task AssertStartupRejects(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelCompression(configuration))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    private sealed class FakeCompressor : IPayloadCompressor
    {
        public CompressionAlgorithm Algorithm => CompressionAlgorithm.Brotli;

        public CompressionFraming Framing => CompressionFraming.Raw;

        public byte[] Compress(byte[] data) => data;

        public byte[] Compress(ReadOnlySpan<byte> data) => data.ToArray();

        public void Compress(ReadOnlySpan<byte> data, IBufferWriter<byte> output) => output.Write(data);

        public void Compress(Stream input, Stream output) => input.CopyTo(output);

        public async ValueTask CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default) =>
            await input.CopyToAsync(output, cancellationToken);

        public Primitives.Results.Result<byte[]> Decompress(byte[] compressed) => compressed;

        public Primitives.Results.Result<byte[]> Decompress(ReadOnlySpan<byte> compressed) => compressed.ToArray();

        public Primitives.Results.Result Decompress(ReadOnlySpan<byte> compressed, IBufferWriter<byte> output)
        {
            output.Write(compressed);
            return Primitives.Results.Result.Success();
        }

        public Primitives.Results.Result Decompress(Stream input, Stream output)
        {
            input.CopyTo(output);
            return Primitives.Results.Result.Success();
        }

        public async ValueTask<Primitives.Results.Result> DecompressAsync(
            Stream input,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            await input.CopyToAsync(output, cancellationToken);
            return Primitives.Results.Result.Success();
        }
    }
}
