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

    public void Dispose() => _keyProvider.Dispose();
}
