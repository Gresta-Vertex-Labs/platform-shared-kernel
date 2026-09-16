using SharedKernel.Cryptography.Random;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeSecureRandomGenerator"/> against <c>ISecureRandomGenerator</c>'s contract: real randomness
/// by default, reproducible output for a seed, and the production argument rules.
/// </summary>
public sealed class FakeSecureRandomGeneratorTests
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    [Fact]
    public void NonSeeded_GetBytes_DiffersAcrossCalls()
    {
        var generator = new FakeSecureRandomGenerator();

        Assert.NotEqual(generator.GetBytes(32), generator.GetBytes(32));
    }

    [Fact]
    public void NonSeeded_GetToken_DiffersAcrossCalls()
    {
        var generator = new FakeSecureRandomGenerator();

        Assert.NotEqual(generator.GetToken(), generator.GetToken());
    }

    [Fact]
    public void Seeded_EveryMember_IsReproducibleForTheSameSeed()
    {
        var first = new FakeSecureRandomGenerator(seed: 42);
        var second = new FakeSecureRandomGenerator(seed: 42);
        Span<byte> firstFill = stackalloc byte[16];
        Span<byte> secondFill = stackalloc byte[16];

        Assert.Equal(first.GetBytes(32), second.GetBytes(32));
        Assert.Equal(first.GetToken(), second.GetToken());
        Assert.Equal(first.GetInt32(1000), second.GetInt32(1000));
        Assert.Equal(first.GetString(Alphabet, 12), second.GetString(Alphabet, 12));
        first.Fill(firstFill);
        second.Fill(secondFill);
        Assert.True(firstFill.SequenceEqual(secondFill));
    }

    [Fact]
    public void Seeded_GetBytes_DiffersAcrossSeeds() =>
        Assert.NotEqual(new FakeSecureRandomGenerator(seed: 1).GetBytes(32), new FakeSecureRandomGenerator(seed: 2).GetBytes(32));

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(64)]
    public void GetBytes_ReturnsExactlyTheRequestedLength(int length)
    {
        Assert.Equal(length, new FakeSecureRandomGenerator().GetBytes(length).Length);
        Assert.Equal(length, new FakeSecureRandomGenerator(seed: 1).GetBytes(length).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetBytes_NonPositiveLength_Throws(int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeSecureRandomGenerator().GetBytes(length));

    [Fact]
    public void Fill_WritesRandomBytesIntoTheWholeBuffer()
    {
        byte[] buffer = new byte[64];

        new FakeSecureRandomGenerator().Fill(buffer);

        Assert.Contains(buffer, b => b != 0);
    }

    [Fact]
    public void GetToken_DefaultsTo32BytesOfUnpaddedBase64Url()
    {
        string token = new FakeSecureRandomGenerator().GetToken();

        Assert.Equal(43, token.Length);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void GetToken_AcceptsTheProductionMinimum() =>
        Assert.Equal(22, new FakeSecureRandomGenerator().GetToken(SecureRandomGenerator.MinimumTokenBytes).Length);

    [Theory]
    [InlineData(SecureRandomGenerator.MinimumTokenBytes - 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetToken_BelowTheProductionMinimum_Throws(int byteCount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeSecureRandomGenerator().GetToken(byteCount));

    [Fact]
    public void GetInt32_StaysWithinRange()
    {
        var generator = new FakeSecureRandomGenerator(seed: 7);

        for (int i = 0; i < 200; i++)
        {
            Assert.InRange(generator.GetInt32(10), 0, 9);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetInt32_NonPositiveUpperBound_Throws(int toExclusive) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeSecureRandomGenerator().GetInt32(toExclusive));

    [Fact]
    public void GetString_UsesOnlyAlphabetCharacters()
    {
        string value = new FakeSecureRandomGenerator().GetString(Alphabet, 256);

        Assert.Equal(256, value.Length);
        Assert.All(value, c => Assert.Contains(c, Alphabet));
    }

    [Fact]
    public void GetString_EmptyAlphabet_Throws() =>
        Assert.Throws<ArgumentException>(() => new FakeSecureRandomGenerator().GetString(ReadOnlySpan<char>.Empty, 4));

    [Fact]
    public void GetString_NonPositiveLength_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeSecureRandomGenerator().GetString(Alphabet, 0));
}
