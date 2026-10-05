using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encode_Rfc4648Vectors_MatchExpectedWithoutPadding(string input, string expected)
    {
        Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(input)));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MY", "f")]
    [InlineData("MZXQ", "fo")]
    [InlineData("MZXW6", "foo")]
    [InlineData("MZXW6YQ", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI", "foobar")]
    public void Decode_Rfc4648Vectors_MatchExpected(string input, string expected)
    {
        Assert.Equal(Encoding.ASCII.GetBytes(expected), Base32.Decode(input).Value);
    }

    [Theory]
    [InlineData("MY======", "f")]
    [InlineData("MZXQ====", "fo")]
    [InlineData("MZXW6===", "foo")]
    [InlineData("MZXW6YQ=", "foob")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    public void Decode_PaddedInput_IsAccepted(string input, string expected)
    {
        Assert.Equal(Encoding.ASCII.GetBytes(expected), Base32.Decode(input).Value);
    }

    [Theory]
    [InlineData("mzxw6ytboi")]
    [InlineData("MzXw6YtBoI")]
    public void Decode_LowercaseInput_IsAccepted(string input)
    {
        Assert.Equal("foobar"u8.ToArray(), Base32.Decode(input).Value);
    }

    [Fact]
    public void EncodeDecode_RandomData_RoundTrips()
    {
        for (int length = 0; length <= 64; length++)
        {
            byte[] data = RandomNumberGenerator.GetBytes(length);

            string encoded = Base32.Encode(data);

            Assert.Equal((length * 8 + 4) / 5, encoded.Length);
            Assert.Equal(data, Base32.Decode(encoded).Value);
        }
    }

    [Theory]
    [InlineData("MZ1W")]
    [InlineData("MZXW 6")]
    [InlineData("MZ=XW")]
    [InlineData("MZXW8")]
    [InlineData("MZXW-6")]
    public void Decode_InvalidCharacter_ReturnsInvalidBase32Encoding(string input)
    {
        ResultAssert.Failure(Base32.Decode(input), CryptographyErrorCodes.InvalidBase32Encoding, ErrorType.Validation);
    }

    [Fact]
    public void Decode_NonAsciiLetterThatUppercasesIntoAlphabet_ReturnsInvalidBase32Encoding()
    {
        ResultAssert.Failure(Base32.Decode(new string('\u017F', 8)), CryptographyErrorCodes.InvalidBase32Encoding, ErrorType.Validation);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("ABC")]
    [InlineData("ABCDEF")]
    [InlineData("MZXW6YTBO")]
    public void Decode_ImpossibleLength_ReturnsInvalidBase32Encoding(string input)
    {
        ResultAssert.Failure(Base32.Decode(input), CryptographyErrorCodes.InvalidBase32Encoding, ErrorType.Validation);
    }

    [Theory]
    [InlineData("MZ")]
    [InlineData("MZXR")]
    [InlineData("MZXW7")]
    [InlineData("MZXW6YR")]
    [InlineData("MZXW6YTBOJ")]
    public void Decode_NonZeroTrailingBits_ReturnsInvalidBase32Encoding(string input)
    {
        ResultAssert.Failure(Base32.Decode(input), CryptographyErrorCodes.InvalidBase32Encoding, ErrorType.Validation);
    }

    [Fact]
    public void Decode_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Base32.Decode(null!));
    }
}
