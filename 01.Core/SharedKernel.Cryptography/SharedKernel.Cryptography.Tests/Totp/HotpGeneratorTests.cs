using System.Text;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class HotpGeneratorTests
{
    private static readonly byte[] Rfc4226Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    private readonly HotpGenerator _generator = new();

    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void GenerateCode_Rfc4226AppendixD_MatchesExpected(long counter, string expected)
    {
        Assert.Equal(expected, _generator.GenerateCode(Rfc4226Secret, counter));
        Assert.True(_generator.ValidateCode(Rfc4226Secret, expected, counter));
    }

    [Fact]
    public void GenerateCode_EightDigits_PadsWithLeadingZeros()
    {
        for (long counter = 0; counter < 200; counter++)
        {
            string code = _generator.GenerateCode(Rfc4226Secret, counter, digits: 8);

            Assert.Equal(8, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void GenerateCode_SixDigitCodeIsSuffixOfEightDigitCode()
    {
        for (long counter = 0; counter < 20; counter++)
        {
            string six = _generator.GenerateCode(Rfc4226Secret, counter, digits: 6);
            string eight = _generator.GenerateCode(Rfc4226Secret, counter, digits: 8);

            Assert.EndsWith(six, eight, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GenerateCode_AlgorithmsProduceDifferentCodes()
    {
        byte[] secret = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

        string sha1 = _generator.GenerateCode(secret, 1, 8, HotpAlgorithm.Sha1);
        string sha256 = _generator.GenerateCode(secret, 1, 8, HotpAlgorithm.Sha256);
        string sha512 = _generator.GenerateCode(secret, 1, 8, HotpAlgorithm.Sha512);

        Assert.Equal(3, new[] { sha1, sha256, sha512 }.Distinct().Count());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(0)]
    public void GenerateCode_DigitsOutOfRange_Throws(int digits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GenerateCode(Rfc4226Secret, 0, digits));
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.ValidateCode(Rfc4226Secret, "755224", 0, digits));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void GenerateCode_SecretShorterThan16Bytes_Throws(int length)
    {
        Assert.Throws<ArgumentException>(() => _generator.GenerateCode(new byte[length], 0));
        Assert.Throws<ArgumentException>(() => _generator.ValidateCode(new byte[length], "123456", 0));
    }

    [Fact]
    public void GenerateCode_SecretOf16Bytes_IsAccepted()
    {
        Assert.Equal(6, _generator.GenerateCode(new byte[16], 0).Length);
    }

    [Fact]
    public void GenerateCode_NegativeCounter_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GenerateCode(Rfc4226Secret, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.ValidateCode(Rfc4226Secret, "755224", -1));
    }

    [Fact]
    public void GenerateCode_UndefinedAlgorithm_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GenerateCode(Rfc4226Secret, 0, 6, (HotpAlgorithm)7));
    }

    [Fact]
    public void GenerateCode_MaximumCounter_IsAccepted()
    {
        Assert.Equal(6, _generator.GenerateCode(Rfc4226Secret, long.MaxValue).Length);
    }

    [Theory]
    [InlineData("755 224")]
    [InlineData("755-224")]
    [InlineData(" 755224 ")]
    [InlineData("7-5-5 2-2-4")]
    public void ValidateCode_SpacesAndHyphens_AreIgnored(string code)
    {
        Assert.True(_generator.ValidateCode(Rfc4226Secret, code, 0));
    }

    [Theory]
    [InlineData("75522a")]
    [InlineData("755_224")]
    [InlineData("75522")]
    [InlineData("7552240")]
    [InlineData("")]
    [InlineData("287082")]
    [InlineData("７５５２２４")]
    [InlineData("755224                                ")]
    public void ValidateCode_WrongOrMalformedCode_ReturnsFalse(string code)
    {
        Assert.False(_generator.ValidateCode(Rfc4226Secret, code, 0));
    }

    [Fact]
    public void ValidateCode_NullCode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _generator.ValidateCode(Rfc4226Secret, null!, 0));
    }
}
