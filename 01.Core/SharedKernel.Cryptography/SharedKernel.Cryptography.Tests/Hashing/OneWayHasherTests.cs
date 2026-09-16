using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Tests.TestDoubles;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class OneWayHasherTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void Hash_ThenVerify_ReturnsSuccess()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        string hash = hasher.Hash(Password);

        Assert.StartsWith("$pbkdf2-sha256$i=100000$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Password));
    }

    [Fact]
    public void Hash_SameSecretTwice_ProducesDifferentHashes()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.NotEqual(hasher.Hash(Password), hasher.Hash(Password));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFailed()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        string hash = hasher.Hash(Password);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "correct horse battery stapl"));
    }

    [Fact]
    public void Hash_EmptySecret_Throws()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.Throws<ArgumentException>(() => hasher.Hash(string.Empty));
        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
    }

    [Fact]
    public void Verify_EmptySecret_ReturnsFailed()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        string hash = hasher.Hash(Password);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, string.Empty));
    }

    [Fact]
    public void Verify_NullArguments_Throw()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.Throws<ArgumentNullException>(() => hasher.Verify(null!, Password));
        Assert.Throws<ArgumentNullException>(() => hasher.Verify("$pbkdf2-sha256$i=1$c2FsdHNhbHQ$aGFzaA", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("$pbkdf2-sha256$i=100000$$")]
    [InlineData("$pbkdf2-sha256$i=100000$c2FsdHNhbHQ$aGFzaA")]
    [InlineData("AQAAAAE=")]
    public void Verify_MalformedHash_ReturnsFailed(string hash)
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Password));
    }

    [Fact]
    public void Verify_UnknownAlgorithmId_ReturnsFailed()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        PhcHashString.TryParse(hasher.Hash(Password), out PhcHashString? parsed);
        var unknown = new PhcHashString("argon2id", 19, parsed!.Parameters, parsed.Salt, parsed.Hash);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(unknown.ToString(), Password));
    }

    [Fact]
    public void Verify_UnpairedSurrogateSecret_ReturnsFailed()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        string hash = hasher.Hash(Password);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "abc\uD800"));
    }

    [Theory]
    [InlineData("ﬁ", "fi")]
    [InlineData("fi", "ﬁ")]
    [InlineData("Å", "Å")]
    [InlineData("ＡＢＣ", "ABC")]
    public void Verify_SecretsEqualUnderNfkc_ReturnsSuccess(string hashed, string presented)
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        string hash = hasher.Hash(hashed);

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, presented));
    }

    [Fact]
    public void Hash_WithPepper_AddsPepperIdParameterAndVerifies()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);

        string hash = hasher.Hash(Password);

        Assert.StartsWith("$pbkdf2-sha256$i=100000,k=p1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Password));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Password + "!"));
    }

    [Fact]
    public void Verify_PepperedHash_IsKeyedWithThePepper()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);
        PhcHashString.TryParse(hasher.Hash(Password), out PhcHashString? peppered);

        string withoutPepperId = peppered!.WithoutParameter("k").ToString();

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(withoutPepperId, Password));
    }

    [Fact]
    public void Verify_PepperIdNotConfigured_ReturnsFailed()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);
        string hash = hasher.Hash(Password);

        options.CurrentValue.OneWayHashing.CurrentPepperId = null;
        options.CurrentValue.OneWayHashing.Peppers.Clear();

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Password));
    }

    [Fact]
    public void Verify_PepperMaterialChangedUnderSameId_ReturnsFailed()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);
        string hash = hasher.Hash(Password);

        options.CurrentValue.OneWayHashing.Peppers["p1"] = NewPepper();

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Password));
    }

    [Fact]
    public void Verify_PepperRotated_ReturnsSuccessRehashNeededAndNewHashUsesCurrentPepper()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);
        string oldHash = hasher.Hash(Password);

        options.CurrentValue.OneWayHashing.Peppers["p2"] = NewPepper();
        options.CurrentValue.OneWayHashing.CurrentPepperId = "p2";

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(oldHash, Password));
        string newHash = hasher.Hash(Password);
        Assert.Contains(",k=p2$", newHash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(newHash, Password));
    }

    [Fact]
    public void Verify_PepperAddedWhereNoneWasUsed_ReturnsSuccessRehashNeeded()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create();
        OneWayHasher hasher = CreateHasher(options);
        string unpeppered = hasher.Hash(Password);

        options.CurrentValue.OneWayHashing.Peppers["p1"] = NewPepper();
        options.CurrentValue.OneWayHashing.CurrentPepperId = "p1";

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(unpeppered, Password));
    }

    [Fact]
    public void Verify_PepperRemovedFromUseButStillConfigured_ReturnsSuccessRehashNeeded()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);
        string peppered = hasher.Hash(Password);

        options.CurrentValue.OneWayHashing.CurrentPepperId = null;

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(peppered, Password));
    }

    [Fact]
    public void Verify_IterationsChanged_ReturnsSuccessRehashNeeded()
    {
        TestOptionsMonitor<Pbkdf2Options> pbkdf2 = TestCryptographyOptions.Pbkdf2();
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create(), pbkdf2);
        string hash = hasher.Hash(Password);

        pbkdf2.CurrentValue.Iterations = 100_001;

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(hash, Password));
    }

    [Fact]
    public void Verify_ConfiguredAlgorithmChanged_ReturnsSuccessRehashNeeded()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create();
        OneWayHasher hasher = CreateHasher(options, new Sha256TestAlgorithm());
        string pbkdf2Hash = hasher.Hash(Password);

        options.CurrentValue.OneWayHashing.Algorithm = Sha256TestAlgorithm.Id;

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(pbkdf2Hash, Password));
        string upgraded = hasher.Hash(Password);
        Assert.StartsWith($"${Sha256TestAlgorithm.Id}$", upgraded, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(upgraded, Password));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(upgraded, "wrong"));
    }

    [Fact]
    public void Verify_AlgorithmRequestsRehash_ReturnsSuccessRehashNeeded()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o => o.Algorithm = Sha256TestAlgorithm.Id);
        var algorithm = new Sha256TestAlgorithm();
        OneWayHasher hasher = CreateHasher(options, algorithm);
        string hash = hasher.Hash(Password);

        algorithm.RequestRehash = true;

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(hash, Password));
    }

    [Fact]
    public void Verify_AlgorithmReceivesSecretWithoutPepperParameter()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Algorithm = Sha256TestAlgorithm.Id;
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        var algorithm = new Sha256TestAlgorithm();
        OneWayHasher hasher = CreateHasher(options, algorithm);

        string hash = hasher.Hash(Password);

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Password));
        Assert.NotNull(algorithm.LastVerified);
        Assert.False(algorithm.LastVerified.TryGetParameter("k", out _));
    }

    [Fact]
    public void Verify_LegacyBinaryFormat_ReturnsSuccessRehashNeeded()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        string legacy = LegacyHash(Password, iterations: 100_000);

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(legacy, Password));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(legacy, Password + "x"));
    }

    [Fact]
    public void Verify_LegacyBinaryFormatWithPepperConfigured_ReturnsSuccessRehashNeeded()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o =>
        {
            o.Peppers["p1"] = NewPepper();
            o.CurrentPepperId = "p1";
        });
        OneWayHasher hasher = CreateHasher(options);

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(LegacyHash(Password, iterations: 100_000), Password));
    }

    [Fact]
    public void Verify_LegacyHashOfSecretChangedByNfkc_VerifiesWithSameSecret()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());
        const string secret = "passﬁword";

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(LegacyHash(secret, iterations: 100_000), secret));
    }

    [Fact]
    public void Verify_LegacyHash_ComparesRawSecretWithoutNfkcEquivalence()
    {
        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(LegacyHash("passﬁword", iterations: 100_000), "passfiword"));
    }

    [Fact]
    public void Verify_LegacyBinaryFormatWithWrongMarker_ReturnsFailed()
    {
        byte[] bytes = Convert.FromBase64String(LegacyHash(Password, iterations: 100_000));
        bytes[0] = 0x02;

        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(Convert.ToBase64String(bytes), Password));
    }

    [Fact]
    public void Verify_LegacyBinaryFormatTruncated_ReturnsFailed()
    {
        byte[] bytes = Convert.FromBase64String(LegacyHash(Password, iterations: 100_000));

        OneWayHasher hasher = CreateHasher(TestCryptographyOptions.Create());

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(Convert.ToBase64String(bytes[..^1]), Password));
    }

    [Fact]
    public void Constructor_DuplicateAlgorithmIds_Throws()
    {
        var options = TestCryptographyOptions.Create();
        var pbkdf2 = TestCryptographyOptions.Pbkdf2();

        Assert.Throws<ArgumentException>(() => new OneWayHasher(
            [new Pbkdf2OneWayHashAlgorithm(pbkdf2), new Pbkdf2OneWayHashAlgorithm(pbkdf2)],
            options));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var options = TestCryptographyOptions.Create();

        Assert.Throws<ArgumentNullException>(() => new OneWayHasher(null!, options));
        Assert.Throws<ArgumentNullException>(() => new OneWayHasher([], null!));
    }

    [Fact]
    public void Hash_ConfiguredAlgorithmNotRegistered_Throws()
    {
        TestOptionsMonitor<CryptographyOptions> options = TestCryptographyOptions.Create(o => o.Algorithm = "argon2id");
        OneWayHasher hasher = CreateHasher(options);

        Assert.Throws<InvalidOperationException>(() => hasher.Hash(Password));
    }

    private static OneWayHasher CreateHasher(
        TestOptionsMonitor<CryptographyOptions> options,
        params IOneWayHashAlgorithm[] extraAlgorithms) =>
        CreateHasher(options, TestCryptographyOptions.Pbkdf2(), extraAlgorithms);

    private static OneWayHasher CreateHasher(
        TestOptionsMonitor<CryptographyOptions> options,
        TestOptionsMonitor<Pbkdf2Options> pbkdf2,
        params IOneWayHashAlgorithm[] extraAlgorithms) =>
        new([new Pbkdf2OneWayHashAlgorithm(pbkdf2), .. extraAlgorithms], options);

    private static string NewPepper() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string LegacyHash(string secret, int iterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, 32);

        byte[] buffer = new byte[1 + 4 + 2 + salt.Length + subkey.Length];
        buffer[0] = 0x01;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, 4), iterations);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(5, 2), (ushort)salt.Length);
        salt.CopyTo(buffer, 7);
        subkey.CopyTo(buffer, 7 + salt.Length);
        return Convert.ToBase64String(buffer);
    }

    /// <summary>A fast stand-in for a second registered algorithm: SHA-256 over salt and secret.</summary>
    private sealed class Sha256TestAlgorithm : IOneWayHashAlgorithm
    {
        public const string Id = "test-sha256";

        public string AlgorithmId => Id;

        public bool RequestRehash { get; set; }

        public PhcHashString? LastVerified { get; private set; }

        public PhcHashString Hash(ReadOnlySpan<byte> secret)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            return new PhcHashString(Id, null, [], salt, Digest(salt, secret));
        }

        public bool Verify(PhcHashString hash, ReadOnlySpan<byte> secret)
        {
            LastVerified = hash;
            return CryptographicOperations.FixedTimeEquals(Digest(hash.Salt, secret), hash.Hash);
        }

        public bool RequiresRehash(PhcHashString hash) => RequestRehash;

        private static byte[] Digest(ReadOnlySpan<byte> salt, ReadOnlySpan<byte> secret) => SHA256.HashData([.. salt, .. secret]);
    }
}
