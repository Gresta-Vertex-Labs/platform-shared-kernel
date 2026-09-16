using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Konscious.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;
using Xunit;
using static SharedKernel.Cryptography.Argon2.Tests.Argon2TestData;

namespace SharedKernel.Cryptography.Argon2.Tests;

public sealed class Argon2idOneWayHashAlgorithmTests
{
    private static readonly Regex PhcFormat = new(
        @"^\$argon2id\$v=19\$m=7168,t=2,p=1\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Argon2idOneWayHashAlgorithm(null!));
    }

    [Fact]
    public void AlgorithmId_Always_IsArgon2id()
    {
        Assert.Equal("argon2id", CreateAlgorithm().AlgorithmId);
        Assert.Equal("argon2id", Argon2idOneWayHashAlgorithm.Id);
    }

    [Fact]
    public void Hash_ConfiguredCosts_WritesPhcStringWith16ByteSaltAnd32ByteHash()
    {
        PhcHashString hash = CreateAlgorithm().Hash(Password);

        Assert.Matches(PhcFormat, hash.ToString());
        Assert.Equal(16, hash.Salt.Length);
        Assert.Equal(32, hash.Hash.Length);
        Assert.Equal(19, hash.Version);
    }

    [Fact]
    public void Hash_SameSecretTwice_UsesDifferentSalts()
    {
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();

        PhcHashString first = algorithm.Hash(Password);
        PhcHashString second = algorithm.Hash(Password);

        Assert.False(first.Salt.SequenceEqual(second.Salt));
        Assert.NotEqual(first.ToString(), second.ToString());
    }

    [Fact]
    public void Hash_CostsChangedAtRuntime_UsesCurrentCosts()
    {
        var monitor = new TestOptionsMonitor<Argon2Options>(new Argon2Options { MemorySizeKb = 7_168, Iterations = 2 });
        var algorithm = new Argon2idOneWayHashAlgorithm(monitor);

        monitor.CurrentValue = new Argon2Options { MemorySizeKb = 8_192, Iterations = 3, DegreeOfParallelism = 2 };
        PhcHashString hash = algorithm.Hash(Password);

        Assert.StartsWith("$argon2id$v=19$m=8192,t=3,p=2$", hash.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_CorrectSecret_ReturnsTrue()
    {
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();
        PhcHashString hash = algorithm.Hash(Password);

        Assert.True(algorithm.Verify(hash, Password));
    }

    [Fact]
    public void Verify_ParsedFromString_ReturnsTrue()
    {
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();
        string stored = algorithm.Hash(Password).ToString();

        Assert.True(PhcHashString.TryParse(stored, out PhcHashString? parsed));
        Assert.True(algorithm.Verify(parsed, Password));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();
        PhcHashString hash = algorithm.Hash(Password);

        Assert.False(algorithm.Verify(hash, Encoding.UTF8.GetBytes("correct horse battery stapl3")));
    }

    [Fact]
    public void Verify_HashFromDifferentCosts_VerifiesWithStoredCosts()
    {
        PhcHashString hash = CreateAlgorithm(memory: 8_192, iterations: 3, parallelism: 2).Hash(Password);

        Assert.True(CreateAlgorithm().Verify(hash, Password));
    }

    [Fact]
    public void Verify_NullHash_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreateAlgorithm().Verify(null!, Password));
    }

    [Fact]
    public void ReferenceImplementation_Rfc9106Argon2idVector_MatchesPublishedTag()
    {
        byte[] password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        using var argon2 = new Argon2id(password)
        {
            Salt = Enumerable.Repeat((byte)0x02, 16).ToArray(),
            KnownSecret = Enumerable.Repeat((byte)0x03, 8).ToArray(),
            AssociatedData = Enumerable.Repeat((byte)0x04, 12).ToArray(),
            MemorySize = 32,
            Iterations = 3,
            DegreeOfParallelism = 4,
        };

        byte[] tag = argon2.GetBytes(32);

        Assert.Equal("0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659", Convert.ToHexStringLower(tag));
    }

    [Fact]
    public void Verify_HashDerivedByReferenceImplementation_ReturnsTrue()
    {
        byte[] salt = Encoding.UTF8.GetBytes("fixed-salt-value");
        byte[] expected = Derive(Password, salt, 7_168, 2, 1, 32);
        var phc = new PhcHashString("argon2id", 19, [new("m", "7168"), new("t", "2"), new("p", "1")], salt, expected);
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();

        Assert.True(PhcHashString.TryParse(phc.ToString(), out PhcHashString? parsed));
        Assert.True(algorithm.Verify(parsed, Password));
        Assert.False(algorithm.RequiresRehash(parsed));
    }

    [Fact]
    public void Verify_PublishedReferencePhcString_ReturnsTrue()
    {
        const string published = "$argon2id$v=19$m=65536,t=2,p=1$c29tZXNhbHQ$CTFhFdXPJO1aFaMaO6Mm5c8y7cJHAph8ArZWb2GRPPc";
        byte[] password = Encoding.UTF8.GetBytes("password");
        byte[] derived = Derive(password, Encoding.UTF8.GetBytes("somesalt"), 65_536, 2, 1, 32);

        Assert.True(PhcHashString.TryParse(published, out PhcHashString? parsed));
        Assert.Equal("09316115d5cf24ed5a15a31a3ba326e5cf32edc24702987c02b6566f61913cf7", Convert.ToHexStringLower(derived));
        Assert.True(parsed.Hash.SequenceEqual(derived));
        Assert.True(CreateAlgorithm().Verify(parsed, password));
        Assert.False(CreateAlgorithm().Verify(parsed, Encoding.UTF8.GetBytes("Password")));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(18)]
    public void Verify_VersionOtherThan19_ReturnsFalse(int version)
    {
        byte[] salt = Salt(16);
        byte[] hash = Derive(Password, salt, MemorySizeKb, Iterations, Parallelism, 32);

        Assert.True(CreateAlgorithm().Verify(Build(salt, hash), Password));
        Assert.False(CreateAlgorithm().Verify(Build(salt, hash, version: version), Password));
    }

    [Fact]
    public void Verify_VersionMissing_ReturnsFalse()
    {
        byte[] salt = Salt(16);
        byte[] hash = Derive(Password, salt, MemorySizeKb, Iterations, Parallelism, 32);

        Assert.False(CreateAlgorithm().Verify(Build(salt, hash, version: null), Password));
    }

    [Theory]
    [InlineData("4194304")]
    [InlineData("1048577")]
    [InlineData("2147483647")]
    public void Verify_MemoryAboveMaximum_ReturnsFalseWithoutDeriving(string memory)
    {
        PhcHashString hash = Build(Salt(16), Salt(32), memory: memory);
        var stopwatch = Stopwatch.StartNew();

        bool verified = CreateAlgorithm().Verify(hash, Password);

        stopwatch.Stop();
        Assert.False(verified);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Verification took {stopwatch.Elapsed}.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("1000000")]
    public void Verify_IterationsOutOfBounds_ReturnsFalse(string iterations)
    {
        PhcHashString hash = Build(Salt(16), Salt(32), iterations: iterations);
        var stopwatch = Stopwatch.StartNew();

        bool verified = CreateAlgorithm().Verify(hash, Password);

        stopwatch.Stop();
        Assert.False(verified);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Verification took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Verify_ElevenIterationsWithMatchingHash_ReturnsFalse()
    {
        byte[] salt = Salt(16);
        PhcHashString atMaximum = BuildValid(salt, 64, 10, 1);
        PhcHashString aboveMaximum = BuildValid(salt, 64, 11, 1);

        Assert.True(CreateAlgorithm().Verify(atMaximum, Password));
        Assert.False(CreateAlgorithm().Verify(aboveMaximum, Password));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("17")]
    [InlineData("255")]
    public void Verify_ParallelismOutOfBounds_ReturnsFalse(string parallelism)
    {
        PhcHashString hash = Build(Salt(16), Salt(32), memory: "7168", parallelism: parallelism);

        Assert.False(CreateAlgorithm().Verify(hash, Password));
    }

    [Fact]
    public void Verify_SeventeenLanesWithMatchingHash_ReturnsFalse()
    {
        byte[] salt = Salt(16);
        PhcHashString atMaximum = BuildValid(salt, 256, 2, 16);
        PhcHashString aboveMaximum = BuildValid(salt, 272, 2, 17);

        Assert.True(CreateAlgorithm().Verify(atMaximum, Password));
        Assert.False(CreateAlgorithm().Verify(aboveMaximum, Password));
    }

    [Theory]
    [InlineData("15", "2")]
    [InlineData("127", "16")]
    [InlineData("7", "1")]
    public void Verify_MemoryBelowEightTimesParallelism_ReturnsFalse(string memory, string parallelism)
    {
        PhcHashString hash = Build(Salt(16), Salt(32), memory: memory, parallelism: parallelism);

        Assert.False(CreateAlgorithm().Verify(hash, Password));
    }

    [Fact]
    public void Verify_MemoryEqualToEightTimesParallelism_ReturnsTrue()
    {
        PhcHashString hash = BuildValid(Salt(16), 16, 2, 2);

        Assert.True(CreateAlgorithm().Verify(hash, Password));
    }

    [Fact]
    public void Verify_ExtraParameter_ReturnsFalse()
    {
        PhcHashString valid = BuildValid(Salt(16), MemorySizeKb, Iterations, Parallelism);

        Assert.True(CreateAlgorithm().Verify(valid, Password));
        Assert.False(CreateAlgorithm().Verify(valid.WithParameter("data", "abc"), Password));
    }

    [Theory]
    [InlineData("m")]
    [InlineData("t")]
    [InlineData("p")]
    public void Verify_MissingParameter_ReturnsFalse(string parameter)
    {
        PhcHashString valid = BuildValid(Salt(16), MemorySizeKb, Iterations, Parallelism);

        Assert.False(CreateAlgorithm().Verify(valid.WithoutParameter(parameter), Password));
    }

    [Theory]
    [InlineData("m", "x", "t")]
    [InlineData("m", "t", "t2")]
    public void Verify_UnexpectedParameterNames_ReturnsFalse(string first, string second, string third)
    {
        byte[] salt = Salt(16);
        byte[] hash = Derive(Password, salt, MemorySizeKb, Iterations, Parallelism, 32);
        var phc = new PhcHashString("argon2id", 19, [new(first, "7168"), new(second, "2"), new(third, "1")], salt, hash);

        Assert.False(CreateAlgorithm().Verify(phc, Password));
    }

    [Theory]
    [InlineData("07168", "2", "1")]
    [InlineData("7168", "02", "1")]
    [InlineData("7168", "2", "01")]
    public void Verify_LeadingZeroParameter_ReturnsFalse(string memory, string iterations, string parallelism)
    {
        byte[] salt = Salt(16);
        byte[] hash = Derive(Password, salt, MemorySizeKb, Iterations, Parallelism, 32);

        Assert.False(CreateAlgorithm().Verify(Build(salt, hash, memory, iterations, parallelism), Password));
    }

    [Theory]
    [InlineData("+7168")]
    [InlineData("7168.0")]
    [InlineData("abc")]
    public void Verify_NonNumericParameter_ReturnsFalse(string memory)
    {
        PhcHashString hash = Build(Salt(16), Salt(32), memory: memory);

        Assert.False(CreateAlgorithm().Verify(hash, Password));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    public void Verify_SaltLengthAtBounds_ReturnsTrue(int saltLength)
    {
        PhcHashString hash = BuildValid(Salt(saltLength), MemorySizeKb, Iterations, Parallelism);

        Assert.True(CreateAlgorithm().Verify(hash, Password));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(65)]
    public void Verify_SaltLengthOutOfBounds_ReturnsFalse(int saltLength)
    {
        PhcHashString hash = Build(Salt(saltLength), Salt(32));

        Assert.False(CreateAlgorithm().Verify(hash, Password));
    }

    [Fact]
    public void Verify_SixtyFiveByteSaltWithMatchingHash_ReturnsFalse()
    {
        PhcHashString hash = BuildValid(Salt(65), MemorySizeKb, Iterations, Parallelism);

        Assert.False(CreateAlgorithm().Verify(hash, Password));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void Verify_HashLengthAtBounds_ReturnsTrue(int hashLength)
    {
        PhcHashString hash = BuildValid(Salt(16), MemorySizeKb, Iterations, Parallelism, hashLength);

        Assert.True(CreateAlgorithm().Verify(hash, Password));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(65)]
    public void Verify_HashLengthOutOfBoundsWithMatchingHash_ReturnsFalse(int hashLength)
    {
        PhcHashString hash = BuildValid(Salt(16), MemorySizeKb, Iterations, Parallelism, hashLength);

        Assert.False(CreateAlgorithm().Verify(hash, Password));
    }

    [Theory]
    [InlineData("argon2i")]
    [InlineData("argon2d")]
    [InlineData("pbkdf2-sha256")]
    public void Verify_OtherAlgorithmId_ReturnsFalse(string algorithmId)
    {
        byte[] salt = Salt(16);
        byte[] hash = Derive(Password, salt, MemorySizeKb, Iterations, Parallelism, 32);

        Assert.False(CreateAlgorithm().Verify(Build(salt, hash, algorithmId: algorithmId), Password));
    }

    [Fact]
    public void RequiresRehash_MatchingCostsAndSizes_ReturnsFalse()
    {
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();

        Assert.False(algorithm.RequiresRehash(algorithm.Hash(Password)));
        Assert.False(algorithm.RequiresRehash(Build(Salt(16), Salt(32))));
    }

    [Theory]
    [InlineData("8192", "2", "1", 16, 32)]
    [InlineData("7168", "3", "1", 16, 32)]
    [InlineData("7168", "2", "2", 16, 32)]
    [InlineData("7168", "2", "1", 8, 32)]
    [InlineData("7168", "2", "1", 32, 32)]
    [InlineData("7168", "2", "1", 16, 16)]
    [InlineData("7168", "2", "1", 16, 64)]
    public void RequiresRehash_DifferentCostsOrSizes_ReturnsTrue(
        string memory,
        string iterations,
        string parallelism,
        int saltLength,
        int hashLength)
    {
        PhcHashString hash = Build(Salt(saltLength), Salt(hashLength), memory, iterations, parallelism);

        Assert.True(CreateAlgorithm().RequiresRehash(hash));
    }

    [Fact]
    public void RequiresRehash_UnreadableParameters_ReturnsTrue()
    {
        Argon2idOneWayHashAlgorithm algorithm = CreateAlgorithm();

        Assert.True(algorithm.RequiresRehash(Build(Salt(16), Salt(32), memory: "07168")));
        Assert.True(algorithm.RequiresRehash(Build(Salt(16), Salt(32), version: null)));
    }

    [Fact]
    public void RequiresRehash_NullHash_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreateAlgorithm().RequiresRehash(null!));
    }
}
