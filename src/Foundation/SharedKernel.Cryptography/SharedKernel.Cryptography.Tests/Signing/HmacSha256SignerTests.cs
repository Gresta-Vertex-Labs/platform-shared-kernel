using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.Tests.Signing;

public sealed class HmacSha256SignerTests
{
    // RFC 4231 test cases 6 and 7 use a 131-byte key of 0xaa.
    private static readonly byte[] Rfc4231Key = Enumerable.Repeat((byte)0xAA, 131).ToArray();

    private readonly HmacSha256Signer _signer = new();

    [Theory]
    [InlineData(
        "Test Using Larger Than Block-Size Key - Hash Key First",
        "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54")]
    [InlineData(
        "This is a test using a larger than block-size key and a larger than block-size data. The key needs to be hashed before being used by the HMAC algorithm.",
        "9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2")]
    public void Sign_Rfc4231Vectors_MatchExpected(string data, string expectedHex)
    {
        byte[] signature = _signer.Sign(Encoding.ASCII.GetBytes(data), Rfc4231Key);

        Assert.Equal(Convert.FromHexString(expectedHex), signature);
        Assert.True(_signer.Verify(Encoding.ASCII.GetBytes(data), Convert.FromHexString(expectedHex), Rfc4231Key));
    }

    [Fact]
    public void Sign_ReturnsHmacSha256OfData()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] data = RandomNumberGenerator.GetBytes(500);

        byte[] signature = _signer.Sign(data, key);

        Assert.Equal(32, signature.Length);
        Assert.Equal(HMACSHA256.HashData(key, data), signature);
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);

        Assert.True(_signer.Verify("payload"u8, _signer.Sign("payload"u8, key), key));
    }

    [Fact]
    public void Verify_AlteredDataSignatureOrKey_ReturnsFalse()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] signature = _signer.Sign("payload"u8, key);
        byte[] alteredSignature = [.. signature];
        alteredSignature[31] ^= 0x01;
        byte[] otherKey = [.. key];
        otherKey[0] ^= 0x01;

        Assert.False(_signer.Verify("payloaD"u8, signature, key));
        Assert.False(_signer.Verify("payload"u8, alteredSignature, key));
        Assert.False(_signer.Verify("payload"u8, signature, otherKey));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    public void Verify_TruncatedSignature_ReturnsFalse(int length)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] signature = _signer.Sign("payload"u8, key);

        Assert.False(_signer.Verify("payload"u8, signature.AsSpan(0, length), key));
    }

    [Fact]
    public void Verify_SignatureWithExtraBytes_ReturnsFalse()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] signature = [.. _signer.Sign("payload"u8, key), 0x00];

        Assert.False(_signer.Verify("payload"u8, signature, key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(31)]
    public void SignAndVerify_KeyShorterThan32Bytes_Throw(int keyLength)
    {
        byte[] key = new byte[keyLength];

        Assert.Throws<ArgumentException>(() => _signer.Sign("payload"u8, key));
        Assert.Throws<ArgumentException>(() => _signer.Verify("payload"u8, new byte[32], key));
    }
}
