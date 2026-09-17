using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Serialization;

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

/// <summary>Simple DTO used in round-trip tests.</summary>
internal sealed record BrotliTestPayload(string Value, int Number);

/// <summary>
/// Unit tests for <see cref="BrotliCacheSerializer"/>.
/// </summary>
public sealed class BrotliCacheSerializerTests
{
    // Default STJ inner serializer with default options — acts as the "real" inner.
    private static FusionCacheSystemTextJsonSerializer CreateInner() =>
        new FusionCacheSystemTextJsonSerializer();

    private static CacheCompressionOptions DefaultOptions(
        int thresholdBytes = 128,
        CompressionLevel level = CompressionLevel.Fastest) =>
        new() { ThresholdBytes = thresholdBytes, Level = level };

    // -------------------------------------------------------------------------
    // B-04 Test 1: Compressed round-trip (payload above threshold)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SerializeAsync_PayloadAboveThreshold_StartsWithMagicBytes()
    {
        // Arrange
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16); // very low threshold to guarantee triggering
        var sut = new BrotliCacheSerializer(inner, opts);

        var dto = new BrotliTestPayload(new string('x', 500), 42); // large payload

        // Act
        byte[] compressed = await sut.SerializeAsync(dto);

        // Assert — magic bytes must be present
        Assert.True(compressed.Length >= 2, "Compressed payload must be at least 2 bytes long.");
        Assert.Equal(0x42, compressed[0]);
        Assert.Equal(0x52, compressed[1]);
    }

    [Fact]
    public async Task SerializeAsync_ThenDeserializeAsync_PayloadAboveThreshold_RoundTripsCorrectly()
    {
        // Arrange
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16);
        var sut = new BrotliCacheSerializer(inner, opts);

        var original = new BrotliTestPayload(new string('x', 500), 99);

        // Act
        byte[] compressed = await sut.SerializeAsync(original);
        var result = await sut.DeserializeAsync<BrotliTestPayload>(compressed);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original, result);
    }

    // -------------------------------------------------------------------------
    // B-04 Test 2: Passthrough (payload below threshold)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SerializeAsync_PayloadBelowThreshold_NoMagicBytes()
    {
        // Arrange
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 100_000); // very high threshold
        var sut = new BrotliCacheSerializer(inner, opts);

        var dto = new BrotliTestPayload("small", 1);

        // Act
        byte[] result = await sut.SerializeAsync(dto);

        // Assert — no magic bytes because payload is below threshold
        Assert.False(
            result.Length >= 2 && result[0] == 0x42 && result[1] == 0x52,
            "Small payload must not be prefixed with magic bytes.");
    }

    [Fact]
    public async Task SerializeAsync_ThenDeserializeAsync_PayloadBelowThreshold_RoundTripsCorrectly()
    {
        // Arrange
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 100_000);
        var sut = new BrotliCacheSerializer(inner, opts);

        var original = new BrotliTestPayload("passthrough", 7);

        // Act
        byte[] raw = await sut.SerializeAsync(original);
        var result = await sut.DeserializeAsync<BrotliTestPayload>(raw);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original, result);
    }

    // -------------------------------------------------------------------------
    // B-04 Test 3: Backward compatibility — uncompressed bytes without magic prefix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_DataWithoutMagicBytes_DelegatesDirectlyToInner()
    {
        // Arrange: simulate a byte[] that was written before compression was enabled
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16);
        var sut = new BrotliCacheSerializer(inner, opts);

        var original = new BrotliTestPayload("legacy-value", 42);

        // Produce uncompressed bytes via the inner serializer directly
        byte[] uncompressedBytes = inner.Serialize(original);

        // Confirm there are no magic bytes (the STJ bytes won't start with 0x42, 0x52)
        Assert.False(
            uncompressedBytes.Length >= 2 && uncompressedBytes[0] == 0x42 && uncompressedBytes[1] == 0x52,
            "Precondition: inner-serialized bytes must not accidentally start with magic bytes.");

        // Act: deserialize via BrotliCacheSerializer — it should pass through to inner
        var result = await sut.DeserializeAsync<BrotliTestPayload>(uncompressedBytes);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(original, result);
    }

    [Fact]
    public async Task DeserializeAsync_MixedCache_HandlesBothCompressedAndUncompressedCorrectly()
    {
        // Arrange: same serializer processes both old (uncompressed) and new (compressed) entries.
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16);
        var sut = new BrotliCacheSerializer(inner, opts);

        var largeDto = new BrotliTestPayload(new string('a', 500), 1);
        var smallDto = new BrotliTestPayload("tiny", 2);

        byte[] compressedBytes = await sut.SerializeAsync(largeDto);   // above threshold → compressed
        byte[] uncompressedBytes = inner.Serialize(smallDto);           // direct inner → no magic bytes

        // Act
        var largeResult = await sut.DeserializeAsync<BrotliTestPayload>(compressedBytes);
        var smallResult = await sut.DeserializeAsync<BrotliTestPayload>(uncompressedBytes);

        // Assert
        Assert.Equal(largeDto, largeResult);
        Assert.Equal(smallDto, smallResult);
    }

    // -------------------------------------------------------------------------
    // B-04 Test 4: CompressionLevel configuration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SerializeAsync_WithOptimalCompressionLevel_CompressesSuccessfully()
    {
        // Arrange
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16, level: CompressionLevel.Optimal);
        var sut = new BrotliCacheSerializer(inner, opts);

        var dto = new BrotliTestPayload(new string('z', 500), 100);

        // Act
        byte[] compressed = await sut.SerializeAsync(dto);
        var result = await sut.DeserializeAsync<BrotliTestPayload>(compressed);

        // Assert — magic bytes present and value round-trips
        Assert.Equal(0x42, compressed[0]);
        Assert.Equal(0x52, compressed[1]);
        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task SerializeAsync_OptimalVsFastest_OptimalProducesSmallerOrEqualOutput()
    {
        // Both levels must produce valid compressed output; Optimal may be smaller than Fastest.
        var inner = CreateInner();
        var originalDto = new BrotliTestPayload(new string('q', 1000), 77);

        var fastSut = new BrotliCacheSerializer(inner, DefaultOptions(thresholdBytes: 16, level: CompressionLevel.Fastest));
        var optimalSut = new BrotliCacheSerializer(inner, DefaultOptions(thresholdBytes: 16, level: CompressionLevel.Optimal));

        byte[] fast = await fastSut.SerializeAsync(originalDto);
        byte[] optimal = await optimalSut.SerializeAsync(originalDto);

        // Both must round-trip correctly.
        Assert.Equal(originalDto, await fastSut.DeserializeAsync<BrotliTestPayload>(fast));
        Assert.Equal(originalDto, await optimalSut.DeserializeAsync<BrotliTestPayload>(optimal));

        // Optimal must not produce larger output than Fastest (may be equal or smaller).
        Assert.True(optimal.Length <= fast.Length,
            $"Optimal ({optimal.Length} bytes) should not be larger than Fastest ({fast.Length} bytes).");
    }

    // -------------------------------------------------------------------------
    // Sync path coverage
    // -------------------------------------------------------------------------

    [Fact]
    public void Serialize_PayloadAboveThreshold_StartsWithMagicBytes()
    {
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16);
        var sut = new BrotliCacheSerializer(inner, opts);

        var dto = new BrotliTestPayload(new string('y', 400), 55);
        byte[] result = sut.Serialize(dto);

        Assert.Equal(0x42, result[0]);
        Assert.Equal(0x52, result[1]);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsCorrectly()
    {
        var inner = CreateInner();
        var opts = DefaultOptions(thresholdBytes: 16);
        var sut = new BrotliCacheSerializer(inner, opts);

        var original = new BrotliTestPayload(new string('y', 400), 55);
        byte[] compressed = sut.Serialize(original);
        var result = sut.Deserialize<BrotliTestPayload>(compressed);

        Assert.Equal(original, result);
    }

    // -------------------------------------------------------------------------
    // DI registration sanity (AddBrotliCompression)
    // -------------------------------------------------------------------------

    [Fact]
    public void AddBrotliCompression_CanResolveIFusionCacheSerializer_AsBrotliDecorator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");
        builder.AddBrotliCompression(o => o.ThresholdBytes = 512);

        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IFusionCacheSerializer>();

        // The resolved serializer must be a BrotliCacheSerializer (internal type accessible via InternalsVisibleTo)
        Assert.IsType<BrotliCacheSerializer>(serializer);
    }

    [Fact]
    public void CacheCompressionOptions_Defaults_Are1024BytesAndFastest()
    {
        var options = new CacheCompressionOptions();

        Assert.Equal(1024, options.ThresholdBytes);
        Assert.Equal(CompressionLevel.Fastest, options.Level);
    }

    [Fact]
    public void AddBrotliCompression_WithoutConfigure_UsesDefaultOptions_AndWrapsTheStjSerializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test").AddBrotliCompression();

        using var provider = services.BuildServiceProvider();
        var serializer = Assert.IsType<BrotliCacheSerializer>(provider.GetRequiredService<IFusionCacheSerializer>());

        Assert.Equal(1024, serializer.Options.ThresholdBytes);
        Assert.Equal(CompressionLevel.Fastest, serializer.Options.Level);
        Assert.Same(provider.GetRequiredService<FusionCacheSystemTextJsonSerializer>(), serializer.Inner);
    }

    [Fact]
    public void AddBrotliCompression_ConfiguredThresholdAndLevel_ReachTheSerializer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test")
            .AddBrotliCompression(o =>
            {
                o.ThresholdBytes = 2048;
                o.Level = CompressionLevel.SmallestSize;
            });

        using var provider = services.BuildServiceProvider();
        var serializer = Assert.IsType<BrotliCacheSerializer>(provider.GetRequiredService<IFusionCacheSerializer>());

        Assert.Equal(2048, serializer.Options.ThresholdBytes);
        Assert.Equal(CompressionLevel.SmallestSize, serializer.Options.Level);
    }

    [Fact]
    public void Serialize_PayloadExactlyAtThreshold_IsCompressed_OneByteBelow_IsNot()
    {
        var inner = CreateInner();
        var probe = inner.Serialize(new BrotliTestPayload(new string('x', 300), 1));

        var atThreshold = new BrotliCacheSerializer(inner, DefaultOptions(thresholdBytes: probe.Length));
        var aboveThreshold = new BrotliCacheSerializer(inner, DefaultOptions(thresholdBytes: probe.Length + 1));

        byte[] compressed = atThreshold.Serialize(new BrotliTestPayload(new string('x', 300), 1));
        byte[] passthrough = aboveThreshold.Serialize(new BrotliTestPayload(new string('x', 300), 1));

        Assert.Equal([0x42, 0x52], compressed[..2]);
        Assert.Equal(probe, passthrough);
    }

    [Fact]
    public void AddBrotliCompression_WithZeroThreshold_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        Assert.Throws<ArgumentException>(() =>
            builder.AddBrotliCompression(o => o.ThresholdBytes = 0));
    }

    [Fact]
    public void AddBrotliCompression_WithNegativeThreshold_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test");

        Assert.Throws<ArgumentException>(() =>
            builder.AddBrotliCompression(o => o.ThresholdBytes = -1));
    }

    [Fact]
    public void AddBrotliCompression_WithNullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((ICachingBuilder)null!).AddBrotliCompression());
    }
}
