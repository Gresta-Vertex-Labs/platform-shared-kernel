using System.Text.RegularExpressions;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Cryptography.Tests.Totp;

public sealed partial class RecoveryCodeGeneratorTests
{
    private readonly RecoveryCodeGenerator _generator = new(new SecureRandomGenerator());

    [Fact]
    public void GenerateCodes_Default_ReturnsTenDistinctFormattedCodes()
    {
        IReadOnlyList<string> codes = _generator.GenerateCodes();

        Assert.Equal(10, codes.Count);
        Assert.All(codes, code => Assert.Matches(CodeFormat(), code));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    public void GenerateCodes_CountInRange_ReturnsThatManyDistinctCodes(int count)
    {
        IReadOnlyList<string> codes = _generator.GenerateCodes(count);

        Assert.Equal(count, codes.Count);
        Assert.Equal(count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void GenerateCodes_CountOutOfRange_Throws(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GenerateCodes(count));
    }

    [Fact]
    public void GenerateCodes_RandomSourceRepeatsItself_StillReturnsDistinctCodes()
    {
        var random = new ScriptedRandom("AAAAAAAAAA", "AAAAAAAAAA", "BBBBBCCCCC", "AAAAAAAAAA", "2345672345");
        var generator = new RecoveryCodeGenerator(random);

        IReadOnlyList<string> codes = generator.GenerateCodes(3);

        Assert.Equal(["AAAAA-AAAAA", "BBBBB-CCCCC", "23456-72345"], codes);
    }

    [Fact]
    public void GenerateCodes_RequestsTenCharactersFromBase32Alphabet()
    {
        var random = new ScriptedRandom("K7Q2MXF4PA");
        var generator = new RecoveryCodeGenerator(random);

        generator.GenerateCodes(1);

        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567", random.LastAlphabet);
        Assert.Equal(10, random.LastLength);
    }

    [Theory]
    [InlineData("k7q2m-xf4pa ", "K7Q2MXF4PA")]
    [InlineData("K7Q2M-XF4PA", "K7Q2MXF4PA")]
    [InlineData(" k7q2m xf4pa", "K7Q2MXF4PA")]
    [InlineData("--", "")]
    [InlineData("", "")]
    public void Normalize_RemovesSeparatorsAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, RecoveryCodeGenerator.Normalize(input));
    }

    [Fact]
    public void Normalize_NullCode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RecoveryCodeGenerator.Normalize(null!));
    }

    [Fact]
    public void Constructor_NullRandom_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RecoveryCodeGenerator(null!));
    }

    [GeneratedRegex("^[A-Z2-7]{5}-[A-Z2-7]{5}$")]
    private static partial Regex CodeFormat();

    private sealed class ScriptedRandom(params string[] strings) : ISecureRandomGenerator
    {
        private int _next;

        public string? LastAlphabet { get; private set; }

        public int LastLength { get; private set; }

        public byte[] GetBytes(int length) => throw new NotSupportedException();

        public void Fill(Span<byte> destination) => throw new NotSupportedException();

        public int GetInt32(int toExclusive) => throw new NotSupportedException();

        public string GetString(ReadOnlySpan<char> alphabet, int length)
        {
            LastAlphabet = alphabet.ToString();
            LastLength = length;
            return strings[_next++];
        }

        public string GetToken(int byteCount = 32) => throw new NotSupportedException();
    }
}
