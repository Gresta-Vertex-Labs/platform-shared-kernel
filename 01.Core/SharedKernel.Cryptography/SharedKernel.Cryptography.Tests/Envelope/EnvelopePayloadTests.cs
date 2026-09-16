using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Envelope;

namespace SharedKernel.Cryptography.Tests.Envelope;

public sealed class EnvelopePayloadTests
{
    private static readonly byte[] WrappedKey = RandomNumberGenerator.GetBytes(60);
    private static readonly byte[] Nonce = RandomNumberGenerator.GetBytes(12);
    private static readonly byte[] Tag = RandomNumberGenerator.GetBytes(16);
    private static readonly byte[] Ciphertext = RandomNumberGenerator.GetBytes(33);

    [Fact]
    public void Constructor_ValidParts_ExposesThem()
    {
        var payload = new EnvelopePayload("master-1", WrappedKey, Nonce, Ciphertext, Tag);

        Assert.Equal("master-1", payload.MasterKeyId);
        Assert.Equal(WrappedKey, payload.WrappedKey.ToArray());
        Assert.Equal(Nonce, payload.Nonce.ToArray());
        Assert.Equal(Ciphertext, payload.Ciphertext.ToArray());
        Assert.Equal(Tag, payload.Tag.ToArray());
    }

    [Fact]
    public void Constructor_CopiesBuffers()
    {
        byte[] wrapped = [.. WrappedKey];
        byte[] ciphertext = [.. Ciphertext];

        var payload = new EnvelopePayload("master-1", wrapped, Nonce, ciphertext, Tag);
        wrapped[0] ^= 0xFF;
        ciphertext[0] ^= 0xFF;

        Assert.Equal(WrappedKey, payload.WrappedKey.ToArray());
        Assert.Equal(Ciphertext, payload.Ciphertext.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Constructor_WrappedKeyLengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => new EnvelopePayload("master-1", new byte[length], Nonce, Ciphertext, Tag));
    }

    [Fact]
    public void Constructor_WrappedKeyOfMaximumLength_IsAccepted()
    {
        var payload = new EnvelopePayload("master-1", new byte[EnvelopePayload.MaxWrappedKeyLength], Nonce, Ciphertext, Tag);

        Assert.True(EnvelopePayload.TryParse(payload.ToBytes(), out EnvelopePayload? parsed));
        Assert.Equal(4096, parsed.WrappedKey.Length);
    }

    [Theory]
    [InlineData(11, 16)]
    [InlineData(13, 16)]
    [InlineData(12, 15)]
    [InlineData(12, 17)]
    public void Constructor_WrongNonceOrTagSize_Throws(int nonceSize, int tagSize)
    {
        Assert.Throws<ArgumentException>(() => new EnvelopePayload("master-1", WrappedKey, new byte[nonceSize], Ciphertext, new byte[tagSize]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidMasterKeyId_Throws(string masterKeyId)
    {
        Assert.Throws<ArgumentException>(() => new EnvelopePayload(masterKeyId, WrappedKey, Nonce, Ciphertext, Tag));
        Assert.Throws<ArgumentException>(() => new EnvelopePayload(new string('m', 256), WrappedKey, Nonce, Ciphertext, Tag));
    }

    [Fact]
    public void ToBytes_WritesDocumentedLayout()
    {
        var payload = new EnvelopePayload("master-1", WrappedKey, Nonce, Ciphertext, Tag);

        byte[] bytes = payload.ToBytes();

        Assert.Equal(0x02, bytes[0]);
        Assert.Equal(8, bytes[1]);
        Assert.Equal("master-1", Encoding.UTF8.GetString(bytes, 2, 8));
        Assert.Equal(60, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10, 2)));
        Assert.Equal(WrappedKey, bytes[12..72]);
        Assert.Equal(Nonce, bytes[72..84]);
        Assert.Equal(Tag, bytes[84..100]);
        Assert.Equal(Ciphertext, bytes[100..]);
    }

    [Theory]
    [InlineData("master-1", 33)]
    [InlineData("m", 0)]
    [InlineData("anahtar-🔑", 1)]
    public void ToBytes_TryParse_RoundTrips(string masterKeyId, int ciphertextLength)
    {
        var payload = new EnvelopePayload(masterKeyId, WrappedKey, Nonce, RandomNumberGenerator.GetBytes(ciphertextLength), Tag);

        Assert.True(EnvelopePayload.TryParse(payload.ToBytes(), out EnvelopePayload? parsed));
        AssertEqual(payload, parsed);
    }

    [Fact]
    public void ToString_TryParse_RoundTrips()
    {
        var payload = new EnvelopePayload("master-1", WrappedKey, Nonce, Ciphertext, Tag);

        string encoded = payload.ToString();

        Assert.DoesNotContain('=', encoded);
        Assert.True(EnvelopePayload.TryParse(encoded, out EnvelopePayload? parsed));
        AssertEqual(payload, parsed);
    }

    [Fact]
    public void TryParse_WrongVersion_ReturnsFalse()
    {
        byte[] bytes = new EnvelopePayload("master-1", WrappedKey, Nonce, Ciphertext, Tag).ToBytes();
        bytes[0] = 0x01;

        Assert.False(EnvelopePayload.TryParse(bytes, out EnvelopePayload? parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_ZeroMasterKeyIdLength_ReturnsFalse()
    {
        Assert.False(EnvelopePayload.TryParse(Build(Encoding.UTF8.GetBytes(string.Empty), 60, WrappedKey), out _));
    }

    [Fact]
    public void TryParse_WrappedKeyLengthZero_ReturnsFalse()
    {
        Assert.False(EnvelopePayload.TryParse(Build("master-1"u8.ToArray(), 0, []), out _));
    }

    [Fact]
    public void TryParse_WrappedKeyLengthAboveMaximum_ReturnsFalse()
    {
        Assert.False(EnvelopePayload.TryParse(Build("master-1"u8.ToArray(), 4097, new byte[4097]), out _));
    }

    [Fact]
    public void TryParse_EveryTruncation_ReturnsFalse()
    {
        byte[] bytes = new EnvelopePayload("master-1", WrappedKey, Nonce, ReadOnlySpan<byte>.Empty, Tag).ToBytes();

        for (int length = 0; length < bytes.Length; length++)
        {
            Assert.False(EnvelopePayload.TryParse(bytes.AsSpan(0, length), out _), $"Length {length} parsed.");
        }
    }

    [Fact]
    public void TryParse_InvalidUtf8MasterKeyId_ReturnsFalse()
    {
        Assert.False(EnvelopePayload.TryParse(Build([0xC3, 0x28], 60, WrappedKey), out _));
    }

    [Fact]
    public void TryParse_WhitespaceMasterKeyId_ReturnsFalse()
    {
        Assert.False(EnvelopePayload.TryParse(Build("  "u8.ToArray(), 60, WrappedKey), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ag")]
    [InlineData("AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void TryParse_InvalidString_ReturnsFalse(string? encoded)
    {
        Assert.False(EnvelopePayload.TryParse(encoded, out EnvelopePayload? parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("@@@@")]
    [InlineData("not a payload")]
    public void TryParse_CharactersOutsideBase64UrlAlphabet_ReturnsFalse(string encoded)
    {
        Assert.False(EnvelopePayload.TryParse(encoded, out EnvelopePayload? parsed));
        Assert.Null(parsed);
    }

    private static byte[] Build(byte[] masterKeyId, int declaredWrappedKeyLength, byte[] wrappedKey)
    {
        byte[] length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)declaredWrappedKeyLength);
        return [0x02, (byte)masterKeyId.Length, .. masterKeyId, .. length, .. wrappedKey, .. Nonce, .. Tag, .. Ciphertext];
    }

    private static void AssertEqual(EnvelopePayload expected, EnvelopePayload actual)
    {
        Assert.Equal(expected.MasterKeyId, actual.MasterKeyId);
        Assert.Equal(expected.WrappedKey.ToArray(), actual.WrappedKey.ToArray());
        Assert.Equal(expected.Nonce.ToArray(), actual.Nonce.ToArray());
        Assert.Equal(expected.Tag.ToArray(), actual.Tag.ToArray());
        Assert.Equal(expected.Ciphertext.ToArray(), actual.Ciphertext.ToArray());
    }
}
