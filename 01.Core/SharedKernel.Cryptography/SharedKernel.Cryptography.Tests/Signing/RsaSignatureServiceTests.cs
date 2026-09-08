using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Signing;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Signing;

public sealed class RsaSignatureServiceTests : IDisposable
{
    private readonly InMemoryAsymmetricKeyProvider _keyProvider = new();

    [Fact]
    public void Sign_ThenVerify_RoundTripsSuccessfully()
    {
        var service = new RsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");

        byte[] signature = service.Sign(data, "key-1");
        bool verified = service.Verify(data, signature, "key-1");

        Assert.True(verified);
    }

    [Fact]
    public void Verify_WithTamperedData_ReturnsFalse()
    {
        var service = new RsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("original data");
        byte[] signature = service.Sign(data, "key-1");

        byte[] tamperedData = Encoding.UTF8.GetBytes("tampered data");
        bool verified = service.Verify(tamperedData, signature, "key-1");

        Assert.False(verified);
    }

    [Fact]
    public void Verify_WithWrongKey_ReturnsFalse()
    {
        var service = new RsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");
        byte[] signature = service.Sign(data, "key-1");

        bool verified = service.Verify(data, signature, "key-2");

        Assert.False(verified);
    }

    [Fact]
    public void Constructor_NullKeyProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RsaSignatureService(null!));
    }

    // --- Async surface (P-493/WO-081) ---

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_RoundTripsSuccessfully()
    {
        var service = new RsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");

        byte[] signature = await service.SignAsync(data, "key-1");
        bool verified = await service.VerifyAsync(data, signature, "key-1");

        Assert.True(verified);
    }

    [Fact]
    public async Task VerifyAsync_WithTamperedData_ReturnsFalse()
    {
        var service = new RsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("original data");
        byte[] signature = await service.SignAsync(data, "key-1");

        byte[] tamperedData = Encoding.UTF8.GetBytes("tampered data");
        bool verified = await service.VerifyAsync(tamperedData, signature, "key-1");

        Assert.False(verified);
    }

    [Fact]
    public async Task VerifyAsync_WithWrongKey_ReturnsFalse()
    {
        var service = new RsaSignatureService(_keyProvider);
        byte[] data = Encoding.UTF8.GetBytes("data to sign");
        byte[] signature = await service.SignAsync(data, "key-1");

        bool verified = await service.VerifyAsync(data, signature, "key-2");

        Assert.False(verified);
    }

    [Fact]
    public async Task SignAsync_ProducesSignatureVerifiableBySyncVerify_UnderSynchronousProvider()
    {
        // Cross-checks the sync and async members share the same cryptographic core: a signature
        // produced by the async path verifies successfully through the sync path, and vice versa.
        var service = new RsaSignatureService(_keyProvider);
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
        var service = new RsaSignatureService(new NonSynchronousAsymmetricKeyProvider(_keyProvider));
        byte[] data = Encoding.UTF8.GetBytes("data");

        Assert.Throws<NotSupportedException>(() => service.Sign(data, "key-1"));
    }

    [Fact]
    public void Verify_AgainstUnmarkedProvider_ThrowsNotSupportedException()
    {
        var service = new RsaSignatureService(new NonSynchronousAsymmetricKeyProvider(_keyProvider));
        byte[] data = Encoding.UTF8.GetBytes("data");

        Assert.Throws<NotSupportedException>(() => service.Verify(data, [1, 2, 3], "key-1"));
    }

    [Fact]
    public async Task SignAsync_AgainstUnmarkedProvider_StillSucceeds()
    {
        // The *Async members are always usable, regardless of the provider's capability marker.
        var service = new RsaSignatureService(new NonSynchronousAsymmetricKeyProvider(_keyProvider));
        byte[] data = Encoding.UTF8.GetBytes("data");

        byte[] signature = await service.SignAsync(data, "key-1");
        bool verified = await service.VerifyAsync(data, signature, "key-1");

        Assert.True(verified);
    }

    // --- Key-ownership / disposal (P-493/WO-081, T-70) ---

    [Fact]
    public void Sign_ThenVerify_NeverDisposesProviderReturnedRsaInstance()
    {
        using var provider = new DisposeThrowingAsymmetricKeyProvider();
        var service = new RsaSignatureService(provider);
        byte[] data = Encoding.UTF8.GetBytes("data");

        // If either member disposed the instance the provider handed back, the SECOND use of
        // that same cached instance below would surface the dispose-guard's thrown exception
        // (or, absent the guard, a genuine ObjectDisposedException).
        byte[] signature = service.Sign(data, "key-1");
        bool verified = service.Verify(data, signature, "key-1");

        Assert.True(verified);
    }

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_NeverDisposesProviderReturnedRsaInstance()
    {
        using var provider = new DisposeThrowingAsymmetricKeyProvider();
        var service = new RsaSignatureService(provider);
        byte[] data = Encoding.UTF8.GetBytes("data");

        byte[] signature = await service.SignAsync(data, "key-1");
        bool verified = await service.VerifyAsync(data, signature, "key-1");

        Assert.True(verified);
    }

    // --- Minimum key size parity (P-493/WO-081) ---

    [Fact]
    public void Sign_WithBelowMinimumKeySize_Throws()
    {
        using RSA realKey = RSA.Create(2048);
        var provider = new DelegateAsymmetricKeyProvider(rsaFactory: _ => new RsaWithKeySize(realKey, 1024));
        var service = new RsaSignatureService(provider);
        byte[] data = Encoding.UTF8.GetBytes("data");

        Assert.Throws<CryptographicException>(() => service.Sign(data, "key-1"));
    }

    [Fact]
    public void Verify_WithBelowMinimumKeySize_Throws()
    {
        using RSA realKey = RSA.Create(2048);
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = realKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        var provider = new DelegateAsymmetricKeyProvider(rsaFactory: _ => new RsaWithKeySize(realKey, 1024));
        var service = new RsaSignatureService(provider);

        Assert.Throws<CryptographicException>(() => service.Verify(data, signature, "key-1"));
    }

    [Fact]
    public async Task VerifyAsync_WithBelowMinimumKeySize_Throws()
    {
        using RSA realKey = RSA.Create(2048);
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = realKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        var provider = new DelegateAsymmetricKeyProvider(rsaFactory: _ => new RsaWithKeySize(realKey, 1024));
        var service = new RsaSignatureService(provider);

        await Assert.ThrowsAsync<CryptographicException>(() => service.VerifyAsync(data, signature, "key-1").AsTask());
    }

    public void Dispose() => _keyProvider.Dispose();
}
