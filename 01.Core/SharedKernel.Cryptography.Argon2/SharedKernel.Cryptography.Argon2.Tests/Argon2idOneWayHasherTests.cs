using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography.Argon2.Options;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using Xunit;

namespace SharedKernel.Cryptography.Argon2.Tests;

public sealed class Argon2idOneWayHasherTests
{
    // A deliberately small-but-still-in-range configuration (the package's own documented
    // floor — see Argon2CryptographyOptions.MinMemorySizeKb/MinIterations) so the test suite
    // does not pay production-strength Argon2id cost (19 MiB / t=2) on every single test.
    private const int FastMemorySizeKb = Argon2CryptographyOptions.MinMemorySizeKb;
    private const int FastIterations = Argon2CryptographyOptions.MinIterations;
    private const int FastDegreeOfParallelism = 1;

    private static IOptionsMonitor<Argon2CryptographyOptions> CreateOptionsMonitor(
        int memorySizeKb = FastMemorySizeKb, int iterations = FastIterations, int degreeOfParallelism = FastDegreeOfParallelism)
    {
        var monitor = Substitute.For<IOptionsMonitor<Argon2CryptographyOptions>>();
        monitor.CurrentValue.Returns(new Argon2CryptographyOptions
        {
            MemorySizeKb = memorySizeKb,
            Iterations = iterations,
            DegreeOfParallelism = degreeOfParallelism,
        });
        return monitor;
    }

    private static Argon2idOneWayHasher CreateHasher(
        int memorySizeKb = FastMemorySizeKb, int iterations = FastIterations, int degreeOfParallelism = FastDegreeOfParallelism) =>
        new(CreateOptionsMonitor(memorySizeKb, iterations, degreeOfParallelism), new CryptoRandomGenerator());

    [Fact]
    public void Hash_ThenVerify_WithCorrectSecret_ReturnsSuccess()
    {
        Argon2idOneWayHasher hasher = CreateHasher();

        string hash = hasher.Hash("correct-horse-battery-staple");
        HashVerificationResult result = hasher.Verify(hash, "correct-horse-battery-staple");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_WithWrongSecret_ReturnsFailed()
    {
        Argon2idOneWayHasher hasher = CreateHasher();

        string hash = hasher.Hash("correct-horse-battery-staple");
        HashVerificationResult result = hasher.Verify(hash, "wrong-secret");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Theory]
    [InlineData("not-a-valid-hash")]
    [InlineData("")]
    [InlineData("$argon2id$v=19$m=7168,t=2,p=1$onlyfiveparts")]
    [InlineData("$argon2i$v=19$m=7168,t=2,p=1$c2FsdHNhbHQ$c3Via2V5c3Via2V5")] // foreign algorithm (argon2i, not argon2id)
    [InlineData("$argon2id$v=16$m=7168,t=2,p=1$c2FsdHNhbHQ$c3Via2V5c3Via2V5")] // foreign/unsupported version
    [InlineData("$argon2id$v=19$m=0,t=2,p=1$c2FsdHNhbHQ$c3Via2V5c3Via2V5")] // non-positive parameter
    [InlineData("$argon2id$v=19$m=7168,t=2,p=1$not!!base64!!$c3Via2V5c3Via2V5")] // invalid base64 salt
    public void Verify_WithMalformedOrForeignHash_ReturnsFailed_NeverThrows(string malformedHash)
    {
        Argon2idOneWayHasher hasher = CreateHasher();

        HashVerificationResult result = hasher.Verify(malformedHash, "any-secret");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_WithStructurallyValidButUncomputableParameters_ReturnsFailed_NeverThrows()
    {
        // Parses structurally, but m=1 is below Konscious.Security.Cryptography.Argon2id's own
        // 4 KiB minimum memory floor — it throws InvalidOperationException from inside the
        // Argon2 computation itself, which Verify must convert to Failed, never let escape.
        Argon2idOneWayHasher hasher = CreateHasher();
        const string uncomputableHash = "$argon2id$v=19$m=1,t=2,p=1$c2FsdHNhbHQ$c3Via2V5c3Via2V5";

        HashVerificationResult result = hasher.Verify(uncomputableHash, "any-secret");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Hash_ProducesRealPhcStringFormat()
    {
        Argon2idOneWayHasher hasher = CreateHasher(memorySizeKb: 7168, iterations: 3, degreeOfParallelism: 1);

        string hash = hasher.Hash("correct-horse-battery-staple");

        Assert.StartsWith("$argon2id$v=19$m=7168,t=3,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(6, hash.Split('$').Length);
    }

    [Fact]
    public void Hash_ProducesDifferentOutputForSameSecretAcrossCalls()
    {
        Argon2idOneWayHasher hasher = CreateHasher();

        string hash1 = hasher.Hash("same-secret");
        string hash2 = hasher.Hash("same-secret");

        Assert.NotEqual(hash1, hash2); // different random salt each time
    }

    [Fact]
    public void Verify_WhenParametersChangedSinceHashing_ReturnsSuccessRehashNeeded()
    {
        Argon2idOneWayHasher hasher = CreateHasher(memorySizeKb: 7168, iterations: 2);
        string hash = hasher.Hash("correct-horse-battery-staple");

        Argon2idOneWayHasher upgradedHasher = CreateHasher(memorySizeKb: 9216, iterations: 4);
        HashVerificationResult result = upgradedHasher.Verify(hash, "correct-horse-battery-staple");

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void Verify_WhenParametersUnchanged_ReturnsSuccessNotRehashNeeded()
    {
        Argon2idOneWayHasher hasher = CreateHasher();
        string hash = hasher.Hash("correct-horse-battery-staple");

        HashVerificationResult result = hasher.Verify(hash, "correct-horse-battery-staple");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Hash_NullSecret_Throws()
    {
        Argon2idOneWayHasher hasher = CreateHasher();

        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
    }

    [Fact]
    public void Verify_NullHash_Throws()
    {
        Argon2idOneWayHasher hasher = CreateHasher();

        Assert.Throws<ArgumentNullException>(() => hasher.Verify(null!, "secret"));
    }

    [Fact]
    public void Verify_NullSecret_Throws()
    {
        Argon2idOneWayHasher hasher = CreateHasher();
        string hash = hasher.Hash("correct-horse-battery-staple");

        Assert.Throws<ArgumentNullException>(() => hasher.Verify(hash, null!));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Argon2idOneWayHasher(null!, new CryptoRandomGenerator()));
    }

    [Fact]
    public void Constructor_NullRandom_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Argon2idOneWayHasher(CreateOptionsMonitor(), null!));
    }

    /// <summary>
    /// Proves <see cref="IOneWayHasher"/> is genuinely secret-agnostic even through the Argon2id
    /// implementation — an API-key-shaped secret roundtrips identically to a password-shaped one.
    /// </summary>
    [Fact]
    public void Hash_ThenVerify_WithApiKeySecret_ReturnsSuccess()
    {
        Argon2idOneWayHasher hasher = CreateHasher();
        const string apiKey = "sk_live_4f8a9c2e6b1d4f7a9c2e6b1d4f7a9c2e";

        string hash = hasher.Hash(apiKey);

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, apiKey));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "sk_live_wrongkeywrongkeywrongkey"));
    }

    // ---- P-524/WO-083: key-material zeroization ----

    /// <summary>
    /// Proves — with a genuine runtime check against actual bytes, never merely that the code
    /// compiles or references <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory"/> —
    /// that the <c>subkey</c> buffer <see cref="Argon2idOneWayHasher.Hash(string)"/> derives is
    /// zeroed in place before the public call returns. Uses the internal, test-only
    /// capture-before-zeroing overload (gated via <c>InternalsVisibleTo</c>) to grab the exact
    /// same array reference the production code path zeroes — a copy would prove nothing.
    /// </summary>
    [Fact]
    public void Hash_ZeroesTheDerivedSubkeyBuffer_BeforeReturning()
    {
        Argon2idOneWayHasher hasher = CreateHasher();
        byte[]? capturedSubkey = null;

        string hash = hasher.Hash("correct-horse-battery-staple", buffer => capturedSubkey = buffer);

        Assert.NotNull(hash);
        Assert.NotNull(capturedSubkey);
        Assert.NotEmpty(capturedSubkey);
        Assert.All(capturedSubkey, b => Assert.Equal(0, b));
    }
}
