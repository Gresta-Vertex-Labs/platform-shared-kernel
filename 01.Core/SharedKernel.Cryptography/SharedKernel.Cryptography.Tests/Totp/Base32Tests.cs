using System.Text;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Covers <see cref="Base32"/> (C-68/T-55): RFC 4648 §10 published test vectors, round-trip
/// correctness, and the "never throws on malformed input" contract.
/// </summary>
public sealed class Base32Tests
{
    // RFC 4648 §10 published Base32 test vectors, padding stripped (this codec is unpadded).
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_MatchesRfc4648PublishedVectors(string input, string expected)
    {
        byte[] data = Encoding.ASCII.GetBytes(input);

        string actual = Base32.Encode(data);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MY", "f")]
    [InlineData("MZXQ", "fo")]
    [InlineData("MZXW6", "foo")]
    [InlineData("MZXW6YQ", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI", "foobar")]
    public void Decode_MatchesRfc4648PublishedVectors(string encoded, string expectedAscii)
    {
        Result<byte[]> result = Base32.Decode(encoded);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedAscii, Encoding.ASCII.GetString(result.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(64)]
    public void EncodeThenDecode_RoundTrips(int byteLength)
    {
        byte[] original = new byte[byteLength];
        for (int i = 0; i < byteLength; i++)
        {
            original[i] = (byte)(i * 7 + 3);
        }

        string encoded = Base32.Encode(original);
        Result<byte[]> decoded = Base32.Decode(encoded);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(original, decoded.Value);
    }

    [Fact]
    public void Decode_IsCaseInsensitive()
    {
        Result<byte[]> upper = Base32.Decode("MZXW6YTBOI");
        Result<byte[]> lower = Base32.Decode("mzxw6ytboi");

        Assert.True(upper.IsSuccess);
        Assert.True(lower.IsSuccess);
        Assert.Equal(upper.Value, lower.Value);
    }

    [Theory]
    [InlineData("1")] // '1' is not in the RFC 4648 Base32 alphabet
    [InlineData("MZXW6YTBOI=")] // padding character present — this codec never expects padding
    [InlineData("MZXW0")] // '0' is not in the alphabet
    [InlineData("MZXW!")] // '!' is not in the alphabet
    public void Decode_InvalidCharacter_ReturnsFailureWithoutThrowing(string malformed)
    {
        Result<byte[]> result = Base32.Decode(malformed);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.InvalidBase32Encoding, result.Error.Code);
    }

    [Theory]
    [InlineData("A")] // length 1, remainder 1 mod 8 — invalid
    [InlineData("AAA")] // length 3, remainder 3 mod 8 — invalid
    [InlineData("AAAAAA")] // length 6, remainder 6 mod 8 — invalid
    public void Decode_InvalidLength_ReturnsFailureWithoutThrowing(string malformed)
    {
        Result<byte[]> result = Base32.Decode(malformed);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.InvalidBase32Encoding, result.Error.Code);
    }

    [Fact]
    public void Encode_NullData_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Base32.Encode(null!));

    [Fact]
    public void Decode_NullText_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Base32.Decode(null!));
}
