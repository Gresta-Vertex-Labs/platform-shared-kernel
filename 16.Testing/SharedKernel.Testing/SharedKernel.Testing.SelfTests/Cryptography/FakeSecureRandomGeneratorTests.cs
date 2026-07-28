using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeSecureRandomGenerator"/> against <c>ISecureRandomGenerator</c>'s
/// documented contract. Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-56.
/// </summary>
public sealed class FakeSecureRandomGeneratorTests
{
    [Fact]
    public void NonSeeded_NextBytes_ProducesDifferentOutputAcrossRepeatedCalls()
    {
        var generator = new FakeSecureRandomGenerator();

        var first = generator.NextBytes(32);
        var second = generator.NextBytes(32);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NonSeeded_NextToken_ProducesDifferentOutputAcrossRepeatedCalls()
    {
        var generator = new FakeSecureRandomGenerator();

        var first = generator.NextToken();
        var second = generator.NextToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Seeded_NextBytes_IsReproducible_ForTheSameSeed()
    {
        var first = new FakeSecureRandomGenerator(seed: 42).NextBytes(32);
        var second = new FakeSecureRandomGenerator(seed: 42).NextBytes(32);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Seeded_NextToken_IsReproducible_ForTheSameSeed()
    {
        var first = new FakeSecureRandomGenerator(seed: 7).NextToken();
        var second = new FakeSecureRandomGenerator(seed: 7).NextToken();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Seeded_NextBytes_DiffersAcrossDifferentSeeds()
    {
        var first = new FakeSecureRandomGenerator(seed: 1).NextBytes(32);
        var second = new FakeSecureRandomGenerator(seed: 2).NextBytes(32);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NextToken_IsUrlSafeBase64_NoPlusSlashOrPadding()
    {
        var generator = new FakeSecureRandomGenerator();

        // A longer token maximizes the chance any '+'/'/' would have appeared under standard
        // Base64 encoding, making the URL-safe substitution/no-padding proof meaningful.
        var token = generator.NextToken(128);

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void NextBytes_ReturnsExactlyRequestedLength()
    {
        var generator = new FakeSecureRandomGenerator();

        Assert.Equal(16, generator.NextBytes(16).Length);
        Assert.Equal(64, new FakeSecureRandomGenerator(seed: 1).NextBytes(64).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextBytes_NonPositiveLength_Throws(int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeSecureRandomGenerator().NextBytes(length));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextToken_NonPositiveLength_Throws(int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeSecureRandomGenerator().NextToken(length));
}
