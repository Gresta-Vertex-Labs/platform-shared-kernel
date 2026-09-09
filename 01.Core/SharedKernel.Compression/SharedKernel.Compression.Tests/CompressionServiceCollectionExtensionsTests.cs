using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Compression.Extensions;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// Covers <see cref="CompressionServiceCollectionExtensions.AddSharedKernelCompression"/>'s
/// <c>TryAdd*</c>-based registration idiom (SK.01.P518) — calling the method more than once must
/// never double-register, and a consumer registration made before this call must always win.
/// </summary>
public sealed class CompressionServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddSharedKernelCompression_CalledTwice_RegistersEachServiceExactlyOnce()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelCompression(EmptyConfiguration());
        services.AddSharedKernelCompression(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IPayloadCompressor>());
        Assert.Single(provider.GetKeyedServices<IPayloadCompressor>(
            CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey));
        Assert.Single(provider.GetKeyedServices<IPayloadCompressor>(
            CompressionServiceCollectionExtensions.GZipPayloadCompressorKey));
    }

    [Fact]
    public void AddSharedKernelCompression_ConsumerFakeRegisteredFirst_WinsOverPlatformDefault()
    {
        var services = new ServiceCollection();
        var fake = new FakePayloadCompressor();

        services.AddSingleton<IPayloadCompressor>(fake);
        services.AddSharedKernelCompression(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IPayloadCompressor>());
    }

    private sealed class FakePayloadCompressor : IPayloadCompressor
    {
        public byte[] Compress(byte[] data) => data;

        public void Compress(Stream input, Stream output) => input.CopyTo(output);

        public Task CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default) =>
            input.CopyToAsync(output, cancellationToken);

        public Result<byte[]> Decompress(byte[] compressed) => compressed;

        public Result Decompress(Stream input, Stream output)
        {
            input.CopyTo(output);
            return Result.Success();
        }

        public async Task<Result> DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
        {
            await input.CopyToAsync(output, cancellationToken);
            return Result.Success();
        }
    }
}
