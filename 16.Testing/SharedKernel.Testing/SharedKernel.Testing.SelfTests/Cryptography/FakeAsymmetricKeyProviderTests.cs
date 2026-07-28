using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeAsymmetricKeyProvider"/> against <c>IAsymmetricKeyProvider</c>'s
/// documented contract. Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-56.
/// </summary>
public sealed class FakeAsymmetricKeyProviderTests
{
    [Fact]
    public void GetRsaKey_SameKeyId_ReturnsDistinctHandles_SharingTheSameKeyMaterial()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        using var first = provider.GetRsaKey("signing-key");
        using var second = provider.GetRsaKey("signing-key");

        Assert.NotSame(first, second);
        Assert.Equal(first.ExportParameters(false).Modulus, second.ExportParameters(false).Modulus);
    }

    [Fact]
    public void GetRsaKey_DifferentKeyIds_ProduceDifferentKeyMaterial()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        using var first = provider.GetRsaKey("key-a");
        using var second = provider.GetRsaKey("key-b");

        Assert.NotEqual(first.ExportParameters(false).Modulus, second.ExportParameters(false).Modulus);
    }

    [Fact]
    public void GetEcdsaKey_SameKeyId_ReturnsDistinctHandles_SharingTheSameKeyMaterial()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        using var first = provider.GetEcdsaKey("signing-key");
        using var second = provider.GetEcdsaKey("signing-key");

        Assert.NotSame(first, second);
        Assert.Equal(first.ExportParameters(false).Q.X, second.ExportParameters(false).Q.X);
        Assert.Equal(first.ExportParameters(false).Q.Y, second.ExportParameters(false).Q.Y);
    }

    [Fact]
    public void GetEcdsaKey_DifferentKeyIds_ProduceDifferentKeyMaterial()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        using var first = provider.GetEcdsaKey("key-a");
        using var second = provider.GetEcdsaKey("key-b");

        Assert.NotEqual(first.ExportParameters(false).Q.X, second.ExportParameters(false).Q.X);
    }

    [Fact]
    public void GetRsaKey_ReturnedHandle_IsSafeToDispose_WithoutInvalidatingTheCachedOriginal()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        var first = provider.GetRsaKey("signing-key");
        first.Dispose();

        // A second call for the same keyId must still succeed — proves the caller received a
        // clone of the cached original, not the original itself.
        using var second = provider.GetRsaKey("signing-key");
        Assert.NotNull(second.ExportParameters(false).Modulus);
    }

    [Fact]
    public void GetEcdsaKey_ReturnedHandle_IsSafeToDispose_WithoutInvalidatingTheCachedOriginal()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        var first = provider.GetEcdsaKey("signing-key");
        first.Dispose();

        using var second = provider.GetEcdsaKey("signing-key");
        Assert.NotNull(second.ExportParameters(false).Q.X);
    }

    [Fact]
    public void Dispose_DisposesCachedRsaKeyPairs_SubsequentAccessToSameKeyIdThrows()
    {
        var provider = new FakeAsymmetricKeyProvider();
        provider.GetRsaKey("rsa-key").Dispose();

        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.GetRsaKey("rsa-key"));
    }

    [Fact]
    public void Dispose_DisposesCachedEcdsaKeyPairs_SubsequentAccessToSameKeyIdThrows()
    {
        var provider = new FakeAsymmetricKeyProvider();
        provider.GetEcdsaKey("ecdsa-key").Dispose();

        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.GetEcdsaKey("ecdsa-key"));
    }
}
