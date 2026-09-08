using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Signing;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Signing;

public sealed class EcdsaSignatureServiceTests : IDisposable
{
    private readonly InMemoryAsymmetricKeyProvider _keyProvider = new();

    [Fact]
    public void Sign_ThenVerify_RoundTripsSuccessfully()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");

        byte[] signature = service.Sign(data, "key-1");
        bool verified = service.Verify(data, signature, "key-1");

        Assert.True(verified);
    }

    [Fact]
    public void Verify_WithTamperedData_ReturnsFalse()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("original data");
        byte[] signature = service.Sign(data, "key-1");

        byte[] tamperedData = Encoding.UTF8.GetBytes("tampered data");
        bool verified = service.Verify(tamperedData, signature, "key-1");

        Assert.False(verified);
    }

    [Fact]
    public void Verify_WithWrongKey_ReturnsFalse()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");
        byte[] signature = service.Sign(data, "key-1");

        bool verified = service.Verify(data, signature, "key-2");

        Assert.False(verified);
    }

    [Fact]
    public void Constructor_NullKeyProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EcdsaSignatureService(null!));
    }

    // --- Async surface (P-493/WO-081) ---

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_RoundTripsSuccessfully()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");

        byte[] signature = await service.SignAsync(data, "key-1");
        bool verified = await service.VerifyAsync(data, signature, "key-1");

        Assert.True(verified);
    }

    [Fact]
    public async Task VerifyAsync_WithTamperedData_ReturnsFalse()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("original data");
        byte[] signature = await service.SignAsync(data, "key-1");

        byte[] tamperedData = Encoding.UTF8.GetBytes("tampered data");
        bool verified = await service.VerifyAsync(tamperedData, signature, "key-1");

        Assert.False(verified);
    }

    [Fact]
    public async Task VerifyAsync_WithWrongKey_ReturnsFalse()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");
        byte[] signature = await service.SignAsync(data, "key-1");

        bool verified = await service.VerifyAsync(data, signature, "key-2");

        Assert.False(verified);
    }

    [Fact]
    public async Task SignAsync_ProducesSignatureVerifiableBySyncVerify_UnderSynchronousProvider()
    {
        var service = new EcdsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("shared core data");

        byte[] asyncSignature = await service.SignAsync(data, "key-1");
        bool verifiedSync = service.Verify(data, asyncSignature, "key-1");

        byte[] syncSignature = service.Sign(data, "key-1");
        bool verifiedAsync = await service.VerifyAsync(data, syncSignature, "key-1");

        Assert.True(verifiedSync);
        Assert.True(verifiedAsync);
    }

    [Fact]
    public void Sign_AgainstUnmarkedProvider_ThrowsNotSupportedException()
    {
        var service = new EcdsaSignatureService(new NonSynchronousAsymmetricKeyProvider(_keyProvider));
        byte[] data = Encoding.UTF8.GetBytes("data");

        Assert.Throws<NotSupportedException>(() => service.Sign(data, "key-1"));
    }

    [Fact]
    public void Verify_AgainstUnmarkedProvider_ThrowsNotSupportedException()
    {
        var service = new EcdsaSignatureService(new NonSynchronousAsymmetricKeyProvider(_keyProvider));
        byte[] data = Encoding.UTF8.GetBytes("data");

        Assert.Throws<NotSupportedException>(() => service.Verify(data, [1, 2, 3], "key-1"));
    }

    [Fact]
    public async Task SignAsync_AgainstUnmarkedProvider_StillSucceeds()
    {
        var service = new EcdsaSignatureService(new NonSynchronousAsymmetricKeyProvider(_keyProvider));
        byte[] data = Encoding.UTF8.GetBytes("data");

        byte[] signature = await service.SignAsync(data, "key-1");
        bool verified = await service.VerifyAsync(data, signature, "key-1");

        Assert.True(verified);
    }

    // --- Key-ownership / disposal (P-493/WO-081, T-70) ---

    [Fact]
    public void Sign_ThenVerify_NeverDisposesProviderReturnedEcdsaInstance()
    {
        using var provider = new DisposeThrowingAsymmetricKeyProvider();
        var service = new EcdsaSignatureService(provider);
        byte[] data = Encoding.UTF8.GetBytes("data");

        byte[] signature = service.Sign(data, "key-1");
        bool verified = service.Verify(data, signature, "key-1");

        Assert.True(verified);
    }

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_NeverDisposesProviderReturnedEcdsaInstance()
    {
        using var provider = new DisposeThrowingAsymmetricKeyProvider();
        var service = new EcdsaSignatureService(provider);
        byte[] data = Encoding.UTF8.GetBytes("data");

        byte[] signature = await service.SignAsync(data, "key-1");
        bool verified = await service.VerifyAsync(data, signature, "key-1");

        Assert.True(verified);
    }

    // --- Minimum key size parity (P-493/WO-081) ---

    [Fact]
    public void Sign_WithBelowMinimumKeySize_Throws()
    {
        using ECDsa realKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var provider = new DelegateAsymmetricKeyProvider(ecdsaFactory: _ => new EcdsaWithKeySize(realKey, 128));
        var service = new EcdsaSignatureService(provider);
        byte[] data = Encoding.UTF8.GetBytes("data");

        Assert.Throws<CryptographicException>(() => service.Sign(data, "key-1"));
    }

    [Fact]
    public void Verify_WithBelowMinimumKeySize_Throws()
    {
        using ECDsa realKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = realKey.SignData(data, HashAlgorithmName.SHA256);

        var provider = new DelegateAsymmetricKeyProvider(ecdsaFactory: _ => new EcdsaWithKeySize(realKey, 128));
        var service = new EcdsaSignatureService(provider);

        Assert.Throws<CryptographicException>(() => service.Verify(data, signature, "key-1"));
    }

    [Fact]
    public async Task VerifyAsync_WithBelowMinimumKeySize_Throws()
    {
        using ECDsa realKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = realKey.SignData(data, HashAlgorithmName.SHA256);

        var provider = new DelegateAsymmetricKeyProvider(ecdsaFactory: _ => new EcdsaWithKeySize(realKey, 128));
        var service = new EcdsaSignatureService(provider);

        await Assert.ThrowsAsync<CryptographicException>(() => service.VerifyAsync(data, signature, "key-1").AsTask());
    }

    public void Dispose() => _keyProvider.Dispose();
}
