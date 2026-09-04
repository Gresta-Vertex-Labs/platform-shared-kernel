using System.Text;
using SharedKernel.Cryptography.Totp;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Covers <see cref="HotpGenerator"/> (C-69/T-55) against RFC 4226 Appendix D's published test
/// vectors — all 10 counter values, SHA-1, 6-digit codes, the RFC's own ASCII secret
/// "12345678901234567890".
/// </summary>
public sealed class HotpGeneratorTests
{
    // RFC 4226 Appendix D's fixed 20-byte ASCII secret.
    private static readonly byte[] Rfc4226Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    // RFC 4226 Appendix D, "HOTP" column (6-digit truncated decimal), counters 0 through 9.
    public static TheoryData<long, string> Rfc4226Vectors => new()
    {
        { 0, "755224" },
        { 1, "287082" },
        { 2, "359152" },
        { 3, "969429" },
        { 4, "338314" },
        { 5, "254676" },
        { 6, "287922" },
        { 7, "162583" },
        { 8, "399871" },
        { 9, "520489" },
    };

    [Theory]
    [MemberData(nameof(Rfc4226Vectors))]
    public void GenerateCode_MatchesRfc4226AppendixDVectors(long counter, string expectedCode)
    {
        var generator = new HotpGenerator();

        string actual = generator.GenerateCode(Rfc4226Secret, counter);

        Assert.Equal(expectedCode, actual);
    }

    [Theory]
    [MemberData(nameof(Rfc4226Vectors))]
    public void ValidateCode_AcceptsTheMatchingRfc4226Vector(long counter, string expectedCode)
    {
        var generator = new HotpGenerator();

        Assert.True(generator.ValidateCode(Rfc4226Secret, expectedCode, counter));
    }

    [Fact]
    public void ValidateCode_WrongCounter_Rejects()
    {
        var generator = new HotpGenerator();

        // Counter 0's code ("755224") must not validate against counter 1.
        Assert.False(generator.ValidateCode(Rfc4226Secret, "755224", 1));
    }

    [Fact]
    public void ValidateCode_WrongCode_Rejects()
    {
        var generator = new HotpGenerator();

        Assert.False(generator.ValidateCode(Rfc4226Secret, "000000", 0));
    }

    [Fact]
    public void GenerateCode_DifferentCountersProduceDifferentCodes()
    {
        var generator = new HotpGenerator();

        string first = generator.GenerateCode(Rfc4226Secret, 0);
        string second = generator.GenerateCode(Rfc4226Secret, 1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GenerateCode_ResultIsAlwaysExactlyDigitsLong()
    {
        var generator = new HotpGenerator();

        // Counter 2's raw truncated value is small enough to require leading-zero padding
        // ("359152" is fine, but this exercises the PadLeft path more directly at 8 digits).
        string code = generator.GenerateCode(Rfc4226Secret, 2, digits: 8);

        Assert.Equal(8, code.Length);
    }

    [Fact]
    public void GenerateCode_NullSecret_Throws()
    {
        var generator = new HotpGenerator();

        Assert.Throws<ArgumentNullException>(() => generator.GenerateCode(null!, 0));
    }

    [Fact]
    public void ValidateCode_NullCode_Throws()
    {
        var generator = new HotpGenerator();

        Assert.Throws<ArgumentNullException>(() => generator.ValidateCode(Rfc4226Secret, null!, 0));
    }
}
