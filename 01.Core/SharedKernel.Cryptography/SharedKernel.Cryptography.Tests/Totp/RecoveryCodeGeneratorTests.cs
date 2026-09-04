using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Totp;

/// <summary>
/// Covers <see cref="RecoveryCodeGenerator"/> (C-72/T-57) — output count/length correctness and
/// statistical non-repetition, mirroring <see cref="CryptoRandomGenerator"/>'s existing test
/// shape.
/// </summary>
public sealed class RecoveryCodeGeneratorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void GenerateCodes_ReturnsRequestedCount(int count)
    {
        var generator = new RecoveryCodeGenerator(new CryptoRandomGenerator());

        IReadOnlyList<string> codes = generator.GenerateCodes(count);

        Assert.Equal(count, codes.Count);
    }

    [Theory]
    [InlineData(5, 8)] // 5 bytes -> 8-char unpadded Base32
    [InlineData(10, 16)] // 10 bytes -> 16-char unpadded Base32
    public void GenerateCodes_EachCodeHasExpectedLength(int lengthBytes, int expectedCharLength)
    {
        var generator = new RecoveryCodeGenerator(new CryptoRandomGenerator());

        IReadOnlyList<string> codes = generator.GenerateCodes(count: 10, lengthBytes: lengthBytes);

        Assert.All(codes, code => Assert.Equal(expectedCharLength, code.Length));
    }

    [Fact]
    public void GenerateCodes_ProducesStatisticallyNonRepeatingOutput()
    {
        var generator = new RecoveryCodeGenerator(new CryptoRandomGenerator());

        IReadOnlyList<string> codes = generator.GenerateCodes(count: 200, lengthBytes: 5);

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void GenerateCodes_DefaultCountIsTen()
    {
        var generator = new RecoveryCodeGenerator(new CryptoRandomGenerator());

        IReadOnlyList<string> codes = generator.GenerateCodes();

        Assert.Equal(10, codes.Count);
    }

    [Fact]
    public void GenerateCodes_ZeroCount_Throws()
    {
        var generator = new RecoveryCodeGenerator(new CryptoRandomGenerator());

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateCodes(count: 0));
    }

    [Fact]
    public void Constructor_NullRandomGenerator_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RecoveryCodeGenerator(null!));
}
