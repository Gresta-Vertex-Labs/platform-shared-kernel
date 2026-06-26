using SharedKernel.Cryptography.Random;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Random;

public sealed class CryptoRandomGeneratorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void NextBytes_ReturnsArrayOfRequestedLength(int length)
    {
        var generator = new CryptoRandomGenerator();

        byte[] bytes = generator.NextBytes(length);

        Assert.Equal(length, bytes.Length);
    }

    [Fact]
    public void NextBytes_ProducesDifferentOutputAcrossCalls()
    {
        var generator = new CryptoRandomGenerator();

        byte[] first = generator.NextBytes(32);
        byte[] second = generator.NextBytes(32);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NextBytes_ProducesStatisticallyNonRepeatingOutputAcrossManyCalls()
    {
        // A weak or non-CSPRNG source (e.g. System.Random with a time-based seed,
        // or a fixed/short internal state) would be expected to repeat or correlate
        // within a few hundred 32-byte draws. RandomNumberGenerator should not.
        var generator = new CryptoRandomGenerator();
        var seen = new HashSet<string>();

        for (int i = 0; i < 500; i++)
        {
            string encoded = Convert.ToBase64String(generator.NextBytes(32));
            Assert.True(seen.Add(encoded), "Detected a repeated 32-byte sample across 500 draws.");
        }
    }

    [Fact]
    public void NextBytes_NeverProducesAllZeroOutput()
    {
        // System.Random and other weak generators can be coerced into predictable
        // patterns; an all-zero buffer is a degenerate case that should never occur
        // across repeated calls to a real CSPRNG.
        var generator = new CryptoRandomGenerator();

        for (int i = 0; i < 100; i++)
        {
            byte[] bytes = generator.NextBytes(32);
            Assert.Contains(bytes, b => b != 0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextBytes_NonPositiveLength_Throws(int length)
    {
        var generator = new CryptoRandomGenerator();

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.NextBytes(length));
    }

    [Fact]
    public void NextToken_DefaultLength_ProducesUrlSafeToken()
    {
        var generator = new CryptoRandomGenerator();

        string token = generator.NextToken();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void NextToken_ProducesDifferentOutputAcrossCalls()
    {
        var generator = new CryptoRandomGenerator();

        string first = generator.NextToken();
        string second = generator.NextToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NextToken_ProducesStatisticallyNonRepeatingOutputAcrossManyCalls()
    {
        var generator = new CryptoRandomGenerator();
        var seen = new HashSet<string>();

        for (int i = 0; i < 500; i++)
        {
            string token = generator.NextToken();
            Assert.True(seen.Add(token), "Detected a repeated token across 500 draws.");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NextToken_NonPositiveLength_Throws(int length)
    {
        var generator = new CryptoRandomGenerator();

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.NextToken(length));
    }
}
