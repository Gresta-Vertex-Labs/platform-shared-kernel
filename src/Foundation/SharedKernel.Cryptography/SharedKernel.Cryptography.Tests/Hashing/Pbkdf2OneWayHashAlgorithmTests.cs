using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Tests.TestDoubles;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed partial class Pbkdf2OneWayHashAlgorithmTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("correct horse battery staple");

    private readonly TestOptionsMonitor<Pbkdf2Options> _options = TestCryptographyOptions.Pbkdf2();
    private readonly Pbkdf2OneWayHashAlgorithm _algorithm;

    public Pbkdf2OneWayHashAlgorithmTests()
    {
        _algorithm = new Pbkdf2OneWayHashAlgorithm(_options);
    }

    [Fact]
    public void AlgorithmId_IsPbkdf2Sha256()
    {
        Assert.Equal("pbkdf2-sha256", _algorithm.AlgorithmId);
        Assert.Equal(Pbkdf2OneWayHashAlgorithm.Id, _algorithm.AlgorithmId);
    }

    [Fact]
    public void Hash_WritesPhcStringWithConfiguredIterations()
    {
        PhcHashString hash = _algorithm.Hash(Secret);

        Assert.Matches(Pbkdf2Format(), hash.ToString());
        Assert.Null(hash.Version);
        Assert.Equal([new KeyValuePair<string, string>("i", "100000")], hash.Parameters);
        Assert.Equal(16, hash.Salt.Length);
        Assert.Equal(32, hash.Hash.Length);
    }

    [Fact]
    public void Hash_OutputMatchesIndependentPbkdf2Derivation()
    {
        PhcHashString hash = _algorithm.Hash(Secret);

        byte[] expected = Rfc2898DeriveBytes.Pbkdf2(Secret, hash.Salt.ToArray(), 100_000, HashAlgorithmName.SHA256, 32);
        Assert.Equal(expected, hash.Hash.ToArray());
    }

    [Fact]
    public void Hash_SameSecretTwice_UsesDifferentSalts()
    {
        PhcHashString first = _algorithm.Hash(Secret);
        PhcHashString second = _algorithm.Hash(Secret);

        Assert.NotEqual(first.Salt.ToArray(), second.Salt.ToArray());
        Assert.NotEqual(first.ToString(), second.ToString());
    }

    [Fact]
    public void Verify_CorrectSecret_ReturnsTrue()
    {
        PhcHashString hash = _algorithm.Hash(Secret);

        Assert.True(_algorithm.Verify(hash, Secret));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        PhcHashString hash = _algorithm.Hash(Secret);

        Assert.False(_algorithm.Verify(hash, Encoding.UTF8.GetBytes("Correct horse battery staple")));
    }

    [Fact]
    public void Verify_SurvivesToStringAndParse()
    {
        string stored = _algorithm.Hash(Secret).ToString();

        Assert.True(PhcHashString.TryParse(stored, out PhcHashString? parsed));
        Assert.True(_algorithm.Verify(parsed, Secret));
    }

    [Fact]
    public void Verify_OneIteration_IsAccepted()
    {
        Assert.True(_algorithm.Verify(Derive(iterations: 1), Secret));
    }

    [Fact]
    public void Verify_ZeroIterations_ReturnsFalse()
    {
        PhcHashString hash = Derive(iterations: 1).WithParameter("i", "0");

        Assert.False(_algorithm.Verify(hash, Secret));
    }

    [Fact]
    public void Verify_IterationsAboveMaximum_ReturnsFalseWithoutDeriving()
    {
        PhcHashString hash = Derive(iterations: 1)
            .WithParameter("i", (Pbkdf2Options.MaximumIterations + 1).ToString(CultureInfo.InvariantCulture));

        var stopwatch = Stopwatch.StartNew();
        bool result = _algorithm.Verify(hash, Secret);
        stopwatch.Stop();

        Assert.False(result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Rejection took {stopwatch.Elapsed}.");
    }

    [Theory]
    [InlineData("2147483647")]
    [InlineData("99999999999")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Verify_MalformedOrOutOfRangeIterations_ReturnsFalse(string iterations)
    {
        PhcHashString hash = Derive(iterations: 1).WithParameter("i", iterations);

        Assert.False(_algorithm.Verify(hash, Secret));
    }

    [Theory]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Verify_SaltLength_IsBounded(int saltLength, bool expected)
    {
        Assert.Equal(expected, _algorithm.Verify(Derive(iterations: 1, saltLength: saltLength), Secret));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Verify_HashLengthOtherThan32_ReturnsFalse(int hashLength)
    {
        Assert.False(_algorithm.Verify(Derive(iterations: 1, hashLength: hashLength), Secret));
    }

    [Fact]
    public void Verify_ExtraParameter_ReturnsFalse()
    {
        Assert.False(_algorithm.Verify(Derive(iterations: 1).WithParameter("m", "1"), Secret));
    }

    [Fact]
    public void Verify_MissingIterations_ReturnsFalse()
    {
        PhcHashString valid = Derive(iterations: 1);
        var hash = new PhcHashString(Pbkdf2OneWayHashAlgorithm.Id, null, [], valid.Salt, valid.Hash);

        Assert.False(_algorithm.Verify(hash, Secret));
    }

    [Fact]
    public void Verify_VersionPresent_ReturnsFalse()
    {
        PhcHashString valid = Derive(iterations: 1);
        var hash = new PhcHashString(Pbkdf2OneWayHashAlgorithm.Id, 1, valid.Parameters, valid.Salt, valid.Hash);

        Assert.False(_algorithm.Verify(hash, Secret));
    }

    [Fact]
    public void Verify_OtherAlgorithmId_ReturnsFalse()
    {
        PhcHashString valid = Derive(iterations: 1);
        var hash = new PhcHashString("pbkdf2-sha512", null, valid.Parameters, valid.Salt, valid.Hash);

        Assert.False(_algorithm.Verify(hash, Secret));
    }

    [Fact]
    public void Verify_NullHash_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _algorithm.Verify(null!, Secret));
    }

    [Fact]
    public void RequiresRehash_CurrentIterationsAndSaltLength_ReturnsFalse()
    {
        Assert.False(_algorithm.RequiresRehash(_algorithm.Hash(Secret)));
    }

    [Fact]
    public void RequiresRehash_IterationsChanged_ReturnsTrue()
    {
        PhcHashString hash = _algorithm.Hash(Secret);

        _options.CurrentValue.Iterations = 200_000;

        Assert.True(_algorithm.RequiresRehash(hash));
    }

    [Fact]
    public void RequiresRehash_NonDefaultSaltLength_ReturnsTrue()
    {
        Assert.True(_algorithm.RequiresRehash(Derive(iterations: 100_000, saltLength: 32)));
    }

    [Fact]
    public void RequiresRehash_MissingIterations_ReturnsTrue()
    {
        PhcHashString valid = Derive(iterations: 1);

        Assert.True(_algorithm.RequiresRehash(new PhcHashString(Pbkdf2OneWayHashAlgorithm.Id, null, [], valid.Salt, valid.Hash)));
    }

    [Fact]
    public void RequiresRehash_NullHash_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _algorithm.RequiresRehash(null!));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Pbkdf2OneWayHashAlgorithm(null!));
    }

    private static PhcHashString Derive(int iterations, int saltLength = 16, int hashLength = 32)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(saltLength);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Secret, salt, iterations, HashAlgorithmName.SHA256, hashLength);
        return new PhcHashString(
            Pbkdf2OneWayHashAlgorithm.Id,
            null,
            [new("i", iterations.ToString(CultureInfo.InvariantCulture))],
            salt,
            hash);
    }

    [GeneratedRegex(@"^\$pbkdf2-sha256\$i=100000\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$")]
    private static partial Regex Pbkdf2Format();
}
