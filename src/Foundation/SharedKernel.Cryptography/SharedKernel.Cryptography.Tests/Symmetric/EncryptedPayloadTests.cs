using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class EncryptedPayloadTests
{
    private static readonly byte[] Nonce = RandomNumberGenerator.GetBytes(EncryptedPayload.NonceSize);
    private static readonly byte[] Tag = RandomNumberGenerator.GetBytes(EncryptedPayload.TagSize);
    private static readonly byte[] Ciphertext = RandomNumberGenerator.GetBytes(40);

    [Fact]
    public void Constructor_ValidParts_ExposesThem()
    {
        var payload = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag);

        Assert.Equal("key-1", payload.KeyId);
        Assert.Equal(Nonce, payload.Nonce.ToArray());
        Assert.Equal(Ciphertext, payload.Ciphertext.ToArray());
        Assert.Equal(Tag, payload.Tag.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(13)]
    public void Constructor_WrongNonceSize_Throws(int size)
    {
        Assert.Throws<ArgumentException>(() => new EncryptedPayload("key-1", new byte[size], Ciphertext, Tag));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void Constructor_WrongTagSize_Throws(int size)
    {
        Assert.Throws<ArgumentException>(() => new EncryptedPayload("key-1", Nonce, Ciphertext, new byte[size]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidKeyId_Throws(string keyId)
    {
        Assert.Throws<ArgumentException>(() => new EncryptedPayload(keyId, Nonce, Ciphertext, Tag));
    }

    [Fact]
    public void Constructor_NullOrOverlongKeyId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EncryptedPayload(null!, Nonce, Ciphertext, Tag));
        Assert.Throws<ArgumentException>(() => new EncryptedPayload(new string('k', 256), Nonce, Ciphertext, Tag));
    }

    [Fact]
    public void Constructor_CopiesBuffers()
    {
        byte[] nonce = [.. Nonce];
        byte[] ciphertext = [.. Ciphertext];
        byte[] tag = [.. Tag];

        var payload = new EncryptedPayload("key-1", nonce, ciphertext, tag);
        nonce[0] ^= 0xFF;
        ciphertext[0] ^= 0xFF;
        tag[0] ^= 0xFF;

        Assert.Equal(Nonce, payload.Nonce.ToArray());
        Assert.Equal(Ciphertext, payload.Ciphertext.ToArray());
        Assert.Equal(Tag, payload.Tag.ToArray());
    }

    [Fact]
    public void ToBytes_WritesDocumentedLayout()
    {
        var payload = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag);

        byte[] bytes = payload.ToBytes();

        Assert.Equal(2 + 5 + 12 + 16 + Ciphertext.Length, bytes.Length);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(5, bytes[1]);
        Assert.Equal("key-1", Encoding.UTF8.GetString(bytes, 2, 5));
        Assert.Equal(Nonce, bytes[7..19]);
        Assert.Equal(Tag, bytes[19..35]);
        Assert.Equal(Ciphertext, bytes[35..]);
    }

    [Fact]
    public void ToBytes_ReturnsIndependentBuffer()
    {
        var payload = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag);

        byte[] first = payload.ToBytes();
        first[^1] ^= 0xFF;

        Assert.Equal(Ciphertext, payload.Ciphertext.ToArray());
        Assert.NotEqual(first, payload.ToBytes());
    }

    [Theory]
    [InlineData("key-1", 40)]
    [InlineData("k", 0)]
    [InlineData("anahtar-şğü-🔑", 1)]
    public void ToBytes_TryParse_RoundTrips(string keyId, int ciphertextLength)
    {
        var payload = new EncryptedPayload(keyId, Nonce, RandomNumberGenerator.GetBytes(ciphertextLength), Tag);

        Assert.True(EncryptedPayload.TryParse(payload.ToBytes(), out EncryptedPayload? parsed));
        AssertEqual(payload, parsed);
    }

    [Fact]
    public void ToBytes_TryParse_RoundTripsKeyIdOf255Bytes()
    {
        var payload = new EncryptedPayload(new string('k', 255), Nonce, Ciphertext, Tag);

        Assert.True(EncryptedPayload.TryParse(payload.ToBytes(), out EncryptedPayload? parsed));
        AssertEqual(payload, parsed);
    }

    [Fact]
    public void ToString_TryParse_RoundTrips()
    {
        var payload = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag);

        string encoded = payload.ToString();

        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.True(EncryptedPayload.TryParse(encoded, out EncryptedPayload? parsed));
        AssertEqual(payload, parsed);
    }

    [Fact]
    public void TryParse_WrongVersion_ReturnsFalse()
    {
        byte[] bytes = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag).ToBytes();
        bytes[0] = 0x02;

        Assert.False(EncryptedPayload.TryParse(bytes, out EncryptedPayload? parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_ZeroKeyIdLength_ReturnsFalse()
    {
        byte[] bytes = [0x01, 0x00, .. Nonce, .. Tag, .. Ciphertext];

        Assert.False(EncryptedPayload.TryParse(bytes, out _));
    }

    [Fact]
    public void TryParse_EveryTruncationOfHeader_ReturnsFalse()
    {
        byte[] bytes = new EncryptedPayload("key-1", Nonce, ReadOnlySpan<byte>.Empty, Tag).ToBytes();

        for (int length = 0; length < bytes.Length; length++)
        {
            Assert.False(EncryptedPayload.TryParse(bytes.AsSpan(0, length), out _), $"Length {length} parsed.");
        }
    }

    [Fact]
    public void TryParse_KeyIdLengthBeyondData_ReturnsFalse()
    {
        byte[] bytes = [0x01, 200, .. Encoding.UTF8.GetBytes("key-1"), .. Nonce, .. Tag];

        Assert.False(EncryptedPayload.TryParse(bytes, out _));
    }

    [Fact]
    public void TryParse_InvalidUtf8KeyId_ReturnsFalse()
    {
        byte[] bytes = [0x01, 0x02, 0xC3, 0x28, .. Nonce, .. Tag];

        Assert.False(EncryptedPayload.TryParse(bytes, out _));
    }

    [Fact]
    public void TryParse_WhitespaceKeyId_ReturnsFalse()
    {
        byte[] bytes = [0x01, 0x02, (byte)' ', (byte)'\t', .. Nonce, .. Tag];

        Assert.False(EncryptedPayload.TryParse(bytes, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AQ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void TryParse_InvalidString_ReturnsFalse(string? encoded)
    {
        Assert.False(EncryptedPayload.TryParse(encoded, out EncryptedPayload? parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("@@@@@@@@")]
    [InlineData("not base64url!")]
    [InlineData("AQ==*")]
    public void TryParse_CharactersOutsideBase64UrlAlphabet_ReturnsFalse(string encoded)
    {
        Assert.False(EncryptedPayload.TryParse(encoded, out EncryptedPayload? parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_WellFormedStringOfWrongVersion_ReturnsFalse()
    {
        byte[] bytes = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag).ToBytes();
        bytes[0] = 0x00;

        Assert.False(EncryptedPayload.TryParse(Base64Url.EncodeToString(bytes), out _));
    }

    [Fact]
    public void TryParse_CopiesInput()
    {
        byte[] bytes = new EncryptedPayload("key-1", Nonce, Ciphertext, Tag).ToBytes();
        Assert.True(EncryptedPayload.TryParse(bytes, out EncryptedPayload? parsed));

        Array.Clear(bytes);

        Assert.Equal(Nonce, parsed.Nonce.ToArray());
        Assert.Equal(Tag, parsed.Tag.ToArray());
        Assert.Equal(Ciphertext, parsed.Ciphertext.ToArray());
    }

    private static void AssertEqual(EncryptedPayload expected, EncryptedPayload actual)
    {
        Assert.Equal(expected.KeyId, actual.KeyId);
        Assert.Equal(expected.Nonce.ToArray(), actual.Nonce.ToArray());
        Assert.Equal(expected.Tag.ToArray(), actual.Tag.ToArray());
        Assert.Equal(expected.Ciphertext.ToArray(), actual.Ciphertext.ToArray());
    }
}
