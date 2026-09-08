using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeAsymmetricKeyProvider"/> against <c>IAsymmetricKeyProvider</c>'s
/// documented contract, including its non-caller-owned, cached-instance-reuse shape (P-502/WO-081,
/// D-241). Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-56/T-104.
/// </summary>
public sealed class FakeAsymmetricKeyProviderTests
{
    [Fact]
    public async Task GetRsaKeyAsync_SameKeyId_ReturnsTheSameInstance_NeverAClone()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        var first = await provider.GetRsaKeyAsync("signing-key");
        var second = await provider.GetRsaKeyAsync("signing-key");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetRsaKeyAsync_DifferentKeyIds_ProduceDifferentKeyMaterial()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        var first = await provider.GetRsaKeyAsync("key-a");
        var second = await provider.GetRsaKeyAsync("key-b");

        Assert.NotEqual(first.ExportParameters(false).Modulus, second.ExportParameters(false).Modulus);
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_SameKeyId_ReturnsTheSameInstance_NeverAClone()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        var first = await provider.GetEcdsaKeyAsync("signing-key");
        var second = await provider.GetEcdsaKeyAsync("signing-key");

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_DifferentKeyIds_ProduceDifferentKeyMaterial()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        var first = await provider.GetEcdsaKeyAsync("key-a");
        var second = await provider.GetEcdsaKeyAsync("key-b");

        Assert.NotEqual(first.ExportParameters(false).Q.X, second.ExportParameters(false).Q.X);
    }

    [Fact]
    public async Task GetRsaKeyAsync_DisposingTheReturnedInstance_ThenReusingSameKeyId_SurfacesObjectDisposedException()
    {
        // The exact regression-detection scenario D-241's cached-instance-reuse migration exists to
        // enable: under the OLD fresh-clone-per-call behavior, this would have silently succeeded.
        var provider = new FakeAsymmetricKeyProvider();
        var rsa = await provider.GetRsaKeyAsync("signing-key");

        rsa.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            var same = await provider.GetRsaKeyAsync("signing-key");
            same.ExportParameters(false);
        });
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_DisposingTheReturnedInstance_ThenReusingSameKeyId_SurfacesObjectDisposedException()
    {
        var provider = new FakeAsymmetricKeyProvider();
        var ecdsa = await provider.GetEcdsaKeyAsync("signing-key");

        ecdsa.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            var same = await provider.GetEcdsaKeyAsync("signing-key");
            same.ExportParameters(false);
        });
    }

    [Fact]
    public async Task Dispose_DisposesEveryCachedRsaKeyPair_ExactlyOnceEach()
    {
        var provider = new FakeAsymmetricKeyProvider();
        var rsa = await provider.GetRsaKeyAsync("rsa-key");

        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => rsa.ExportParameters(false));
    }

    [Fact]
    public async Task Dispose_DisposesEveryCachedEcdsaKeyPair_ExactlyOnceEach()
    {
        var provider = new FakeAsymmetricKeyProvider();
        var ecdsa = await provider.GetEcdsaKeyAsync("ecdsa-key");

        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ecdsa.ExportParameters(false));
    }

    [Fact]
    public async Task GetRsaKeyAsync_NullKeyId_Throws()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetRsaKeyAsync(null!));
    }

    [Fact]
    public async Task GetEcdsaKeyAsync_NullKeyId_Throws()
    {
        using var provider = new FakeAsymmetricKeyProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetEcdsaKeyAsync(null!));
    }
}
