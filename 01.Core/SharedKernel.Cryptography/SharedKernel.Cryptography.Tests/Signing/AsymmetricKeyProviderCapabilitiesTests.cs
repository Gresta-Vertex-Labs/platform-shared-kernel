using SharedKernel.Cryptography.Signing;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Signing;

/// <summary>
/// Covers <see cref="AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)"/>
/// (P-493/WO-081): direct marker implementers and unmarked providers. Unlike its symmetric
/// counterpart (<c>EncryptionKeyProviderCapabilities</c>), this is a direct check only — there is
/// no decorator to unwrap, since no caching decorator exists for <see cref="IAsymmetricKeyProvider"/>.
/// </summary>
public sealed class AsymmetricKeyProviderCapabilitiesTests
{
    [Fact]
    public void IsGenuinelySynchronous_ProviderImplementingMarker_ReturnsTrue()
    {
        using var provider = new InMemoryAsymmetricKeyProvider();

        bool result = AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(provider);

        Assert.True(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_UnmarkedProvider_ReturnsFalse()
    {
        using var inner = new InMemoryAsymmetricKeyProvider();
        var provider = new NonSynchronousAsymmetricKeyProvider(inner);

        bool result = AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(provider);

        Assert.False(result);
    }

    [Fact]
    public void IsGenuinelySynchronous_NullProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(null!));
    }
}
