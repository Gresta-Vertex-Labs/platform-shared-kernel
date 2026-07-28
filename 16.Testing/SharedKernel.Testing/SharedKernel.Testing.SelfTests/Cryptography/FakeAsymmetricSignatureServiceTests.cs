using SharedKernel.Cryptography.Extensions;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeAsymmetricSignatureService"/> against
/// <c>IAsymmetricSignatureService</c>'s documented contract. Proven exclusively in
/// <c>SharedKernel.Testing.SelfTests</c> — see <c>16.Testing/state-map.md</c> T-56.
/// </summary>
public sealed class FakeAsymmetricSignatureServiceTests
{
    [Fact]
    public void Sign_ThenVerify_RoundTrips()
    {
        var service = new FakeAsymmetricSignatureService();
        var data = "payload"u8.ToArray();

        var signature = service.Sign(data, "key-1");

        Assert.True(service.Verify(data, signature, "key-1"));
    }

    [Fact]
    public void Verify_TamperedData_ReturnsFalse()
    {
        var service = new FakeAsymmetricSignatureService();
        var signature = service.Sign("payload"u8.ToArray(), "key-1");

        Assert.False(service.Verify("tampered"u8.ToArray(), signature, "key-1"));
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsFalse()
    {
        var service = new FakeAsymmetricSignatureService();
        var data = "payload"u8.ToArray();
        var signature = service.Sign(data, "key-1");
        signature[0] ^= 0xFF;

        Assert.False(service.Verify(data, signature, "key-1"));
    }

    [Fact]
    public void Verify_MismatchedKeyId_ReturnsFalse()
    {
        var service = new FakeAsymmetricSignatureService();
        var data = "payload"u8.ToArray();
        var signature = service.Sign(data, "key-1");

        Assert.False(service.Verify(data, signature, "key-2"));
    }

    [Theory]
    [InlineData("unkeyed-default")]
    [InlineData(CryptographyServiceCollectionExtensions.RsaSignatureServiceKey)]
    [InlineData(CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey)]
    public void SignVerify_BehavesUniformly_AcrossUnkeyedAndBothRealKeyedRegistrationSlotNames(string keyId)
    {
        var service = new FakeAsymmetricSignatureService();
        var data = "payload"u8.ToArray();

        var signature = service.Sign(data, keyId);

        Assert.True(service.Verify(data, signature, keyId));
    }

    [Fact]
    public void SignedPayloads_RecordsEveryCall_InOrder()
    {
        var service = new FakeAsymmetricSignatureService();

        service.Sign("a"u8.ToArray(), "key-1");
        service.Sign("b"u8.ToArray(), "key-2");

        Assert.Equal(2, service.SignedPayloads.Count);
        Assert.Equal("key-1", service.SignedPayloads[0].KeyId);
        Assert.Equal("key-2", service.SignedPayloads[1].KeyId);
    }

    [Fact]
    public void Sign_NullArguments_Throw()
    {
        var service = new FakeAsymmetricSignatureService();

        Assert.Throws<ArgumentNullException>(() => service.Sign(null!, "key-1"));
        Assert.Throws<ArgumentNullException>(() => service.Sign("data"u8.ToArray(), null!));
    }
}
