using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed class TotpSecretTests
{
    private readonly SecureRandomGenerator _random = new();

    [Fact]
    public void Generate_Default_Returns20RandomBytes()
    {
        byte[] first = TotpSecret.Generate(_random);
        byte[] second = TotpSecret.Generate(_random);

        Assert.Equal(TotpSecret.DefaultLength, first.Length);
        Assert.Equal(20, first.Length);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Generate_LengthInRange_ReturnsThatLength(int length)
    {
        Assert.Equal(length, TotpSecret.Generate(_random, length).Length);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(65)]
    [InlineData(0)]
    public void Generate_LengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TotpSecret.Generate(_random, length));
    }

    [Fact]
    public void Generate_NullRandom_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TotpSecret.Generate(null!));
    }

    [Fact]
    public void Generate_SecretIsUsableByGenerator()
    {
        byte[] secret = TotpSecret.Generate(_random, 16);

        Assert.Equal(6, new HotpGenerator().GenerateCode(secret, 1).Length);
    }
}
