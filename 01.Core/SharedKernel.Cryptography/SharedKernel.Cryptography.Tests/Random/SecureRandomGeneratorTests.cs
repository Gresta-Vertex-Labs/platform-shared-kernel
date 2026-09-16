using System.Buffers.Text;
using System.Text.RegularExpressions;
using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.Tests.Random;

public sealed partial class SecureRandomGeneratorTests
{
    private readonly SecureRandomGenerator _generator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(4096)]
    public void GetBytes_ReturnsRequestedLength(int length)
    {
        Assert.Equal(length, _generator.GetBytes(length).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetBytes_NonPositiveLength_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GetBytes(length));
    }

    [Fact]
    public void GetBytes_ConsecutiveCalls_DifferInContent()
    {
        Assert.NotEqual(_generator.GetBytes(32), _generator.GetBytes(32));
    }

    [Fact]
    public void Fill_FillsDestinationWithRandomBytes()
    {
        byte[] first = new byte[64];
        byte[] second = new byte[64];

        _generator.Fill(first);
        _generator.Fill(second);

        Assert.Contains(first, b => b != 0);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Fill_EmptyDestination_DoesNotThrow()
    {
        _generator.Fill(Span<byte>.Empty);
    }

    [Fact]
    public void GetInt32_ReturnsValuesInRangeCoveringEveryValue()
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < 2000; i++)
        {
            int value = _generator.GetInt32(10);
            Assert.InRange(value, 0, 9);
            seen.Add(value);
        }

        Assert.Equal(10, seen.Count);
    }

    [Fact]
    public void GetInt32_UpperBoundOne_AlwaysReturnsZero()
    {
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(0, _generator.GetInt32(1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetInt32_NonPositiveBound_Throws(int toExclusive)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GetInt32(toExclusive));
    }

    [Fact]
    public void GetString_UsesOnlyAlphabetCharacters()
    {
        string value = _generator.GetString("abc", 500);

        Assert.Equal(500, value.Length);
        Assert.All(value, c => Assert.Contains(c, "abc"));
        Assert.Equal(3, value.Distinct().Count());
    }

    [Fact]
    public void GetString_EmptyAlphabet_Throws()
    {
        Assert.Throws<ArgumentException>(() => _generator.GetString(string.Empty, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetString_NonPositiveLength_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GetString("abc", length));
    }

    [Fact]
    public void GetToken_Default_Returns43CharacterBase64UrlToken()
    {
        string token = _generator.GetToken();

        Assert.Matches(Base64UrlToken(), token);
        Assert.Equal(43, token.Length);
        Assert.Equal(32, Base64Url.DecodeFromChars(token).Length);
    }

    [Theory]
    [InlineData(16, 22)]
    [InlineData(64, 86)]
    [InlineData(300, 400)]
    public void GetToken_ByteCount_ReturnsEncodedLength(int byteCount, int expectedLength)
    {
        string token = _generator.GetToken(byteCount);

        Assert.Equal(expectedLength, token.Length);
        Assert.Equal(byteCount, Base64Url.DecodeFromChars(token).Length);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetToken_FewerThan16Bytes_Throws(int byteCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GetToken(byteCount));
    }

    [Fact]
    public void GetToken_ManyCalls_AreUnique()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(tokens.Add(_generator.GetToken()));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex Base64UrlToken();
}
