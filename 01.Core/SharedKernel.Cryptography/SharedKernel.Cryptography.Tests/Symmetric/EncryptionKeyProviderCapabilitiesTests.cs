using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Covers <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(IEncryptionKeyProvider)"/>
/// (P-492/WO-081): direct marker implementers, unmarked providers, and the recursive unwrap
/// through any depth of <see cref="CachedEncryptionKeyProvider"/> decoration.
/// </summary>
public sealed class EncryptionKeyProviderCapabilitiesTests
{
    private static CryptographicKey NewKey(string id) => new(id, RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void IsGenuinelySynchronous_ProviderImplementingMarker_ReturnsTrue()
    {
        var provider = new InMemoryEncryptionKeyProvider();

        bool result = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(provider);

        Assert.True(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_UnmarkedProvider_ReturnsFalse()
    {
        var provider = new ControllableEncryptionKeyProvider(NewKey("v1"));

        bool result = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(provider);

        Assert.False(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_CachedProviderNeverDirectlyImplementsMarker()
    {
        var inner = new InMemoryEncryptionKeyProvider();
        IEncryptionKeyProvider cached = new CachedEncryptionKeyProvider(
            inner, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));

        // Statically verifies the D-69/C-82 design constraint: CachedEncryptionKeyProvider must
        // never itself implement the marker — its safety is entirely a function of what it
        // wraps, resolved via EncryptionKeyProviderCapabilities' recursive unwrap instead.
        Assert.False(cached is ISynchronousEncryptionKeyProvider);
    }

    [Fact]
    public void IsGenuinelySynchronous_CachedProviderWrappingMarkedInner_ReturnsTrue()
    {
        var inner = new InMemoryEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(inner, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));

        bool result = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(cached);

        Assert.True(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_CachedProviderWrappingUnmarkedInner_ReturnsFalse()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var cached = new CachedEncryptionKeyProvider(inner, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));

        bool result = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(cached);

        Assert.False(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_NestedCachedProviderWrappingMarkedLeaf_ResolvesRecursivelyToTrue()
    {
        var leaf = new InMemoryEncryptionKeyProvider();
        var innerCache = new CachedEncryptionKeyProvider(leaf, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));
        var outerCache = new CachedEncryptionKeyProvider(innerCache, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));

        bool result = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(outerCache);

        Assert.True(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_NestedCachedProviderWrappingUnmarkedLeaf_ResolvesRecursivelyToFalse()
    {
        var leaf = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var innerCache = new CachedEncryptionKeyProvider(leaf, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));
        var outerCache = new CachedEncryptionKeyProvider(innerCache, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));

        bool result = EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(outerCache);

        Assert.False(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(null!));
    }

    [Fact]
    public void CachedEncryptionKeyProvider_Inner_ExposesTheWrappedProvider()
    {
        var inner = new InMemoryEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(inner, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(5));

        Assert.Same(inner, cached.Inner);
    }
}
