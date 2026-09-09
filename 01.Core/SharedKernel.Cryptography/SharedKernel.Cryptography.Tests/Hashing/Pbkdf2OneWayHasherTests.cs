using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class Pbkdf2OneWayHasherTests
{
    private static IOptionsMonitor<CryptographyOptions> CreateOptionsMonitor(int iterations)
    {
        var monitor = Substitute.For<IOptionsMonitor<CryptographyOptions>>();
        monitor.CurrentValue.Returns(new CryptographyOptions { Pbkdf2Iterations = iterations });
        return monitor;
    }

    /// <summary>
    /// Mirrors <see cref="Pbkdf2OneWayHasher"/>'s private <c>Encode</c> — duplicated here as a
    /// black-box input-construction helper (the same convention
    /// <c>AzureKeyVaultEncryptionKeyProviderVersionRegistryTests.EncodeLegacyEnvelope</c> uses) so
    /// P-512/WO-083's tests can craft an adversarial/malformed hash string directly, without a
    /// production-code seam.
    /// </summary>
    private static string EncodeForTest(int iterations, byte[] salt, byte[] subkey)
    {
        int length = 1 + 4 + 2 + salt.Length + subkey.Length;
        byte[] buffer = new byte[length];
        Span<byte> span = buffer;

        span[0] = 0x01; // FormatMarker
        BinaryPrimitives.WriteInt32BigEndian(span[1..5], iterations);
        BinaryPrimitives.WriteUInt16BigEndian(span[5..7], (ushort)salt.Length);
        salt.CopyTo(span[7..]);
        subkey.CopyTo(span[(7 + salt.Length)..]);

        return Convert.ToBase64String(buffer);
    }

    [Fact]
    public void Hash_ThenVerify_WithCorrectPassword_ReturnsSuccess()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));

        string hash = hasher.Hash("correct-password");
        HashVerificationResult result = hasher.Verify(hash, "correct-password");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFailed()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));

        string hash = hasher.Hash("correct-password");
        HashVerificationResult result = hasher.Verify(hash, "wrong-password");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_WithMalformedHash_ReturnsFailed()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));

        HashVerificationResult result = hasher.Verify("not-a-valid-hash", "any-password");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Hash_ProducesDifferentOutputForSamePasswordAcrossCalls()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));

        string hash1 = hasher.Hash("same-password");
        string hash2 = hasher.Hash("same-password");

        Assert.NotEqual(hash1, hash2); // different random salt each time
    }

    [Fact]
    public void Verify_WhenIterationCountIncreasedSinceHashing_ReturnsSuccessRehashNeeded()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));
        string hash = hasher.Hash("correct-password");

        var upgradedHasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(2000));
        HashVerificationResult result = upgradedHasher.Verify(hash, "correct-password");

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void Verify_WhenIterationCountUnchanged_ReturnsSuccessNotRehashNeeded()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));
        string hash = hasher.Hash("correct-password");

        HashVerificationResult result = hasher.Verify(hash, "correct-password");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Hash_NullSecret_Throws()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));

        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Pbkdf2OneWayHasher(null!));
    }

    /// <summary>
    /// Proves <see cref="IOneWayHasher"/> is genuinely secret-agnostic, not just a renamed
    /// password hasher: an API-key-shaped secret roundtrips identically to a password-shaped one,
    /// using the exact same Hash/Verify contract and rehash-needed detection.
    /// </summary>
    [Fact]
    public void Hash_ThenVerify_WithApiKeySecret_ReturnsSuccess()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));
        const string apiKey = "sk_live_4f8a9c2e6b1d4f7a9c2e6b1d4f7a9c2e";

        string hash = hasher.Hash(apiKey);

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, apiKey));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "sk_live_wrongkeywrongkeywrongkey"));
    }

    // ---- P-512/WO-083: iteration floor + verify-time ceiling ----

    [Fact]
    public void Verify_IterationCountAboveMaxVerifiableIterations_ReturnsFailed_WithoutRunningTheExpensiveDerive()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(600_000));
        byte[] salt = new byte[16];
        byte[] subkey = new byte[32];
        // Comfortably above the 2,000,000 ceiling — if this were ever actually run through
        // Rfc2898DeriveBytes.Pbkdf2, it would take many seconds; a generous 500ms upper bound is
        // therefore a non-flaky proof the expensive derive never ran.
        string maliciousHash = EncodeForTest(10_000_000, salt, subkey);

        var stopwatch = Stopwatch.StartNew();
        HashVerificationResult result = hasher.Verify(maliciousHash, "any-password");
        stopwatch.Stop();

        Assert.Equal(HashVerificationResult.Failed, result);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"Verify took {stopwatch.ElapsedMilliseconds}ms — the ceiling check must reject before the " +
            "expensive PBKDF2 derive call ever runs.");
    }

    [Fact]
    public void Verify_SubkeyLengthNotExactly32Bytes_ReturnsFailed()
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));
        byte[] salt = new byte[16];

        string tooShort = EncodeForTest(1000, salt, new byte[16]);
        string tooLong = EncodeForTest(1000, salt, new byte[64]);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(tooShort, "any-password"));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(tooLong, "any-password"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Verify_NonPositiveIterationCount_ReturnsFailed_RatherThanThrowing(int iterations)
    {
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(1000));
        string malformedHash = EncodeForTest(iterations, new byte[16], new byte[32]);

        HashVerificationResult result = hasher.Verify(malformedHash, "any-password");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_HashProducedAtCurrentShippedDefault_StillVerifiesSuccessfully()
    {
        // The current 600,000 OWASP-2023+ default must remain fully verifiable under the new
        // 2,000,000 ceiling.
        var hasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(600_000));

        string hash = hasher.Hash("correct-password");
        HashVerificationResult result = hasher.Verify(hash, "correct-password");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_HashProducedAtLegitimateBelowFloorLegacyValue_StillVerifies_NeverRejectedByNewFloor()
    {
        // Simulates a hash stored before P-512 shipped, under a configuration this package
        // historically permitted (no floor existed at all). The new 100,000 floor is enforced
        // only at Hash() time via startup options validation — never retroactively against an
        // already-stored hash's own embedded iteration count.
        var legacyHasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(10_000));
        string legacyHash = legacyHasher.Hash("correct-password");

        HashVerificationResult sameConfigResult = legacyHasher.Verify(legacyHash, "correct-password");
        Assert.Equal(HashVerificationResult.Success, sameConfigResult);

        // A service that has since raised its configured iterations to the current default must
        // still be able to verify (and flag for rehash) the old, below-floor legacy hash.
        var upgradedHasher = new Pbkdf2OneWayHasher(CreateOptionsMonitor(600_000));
        HashVerificationResult upgradedResult = upgradedHasher.Verify(legacyHash, "correct-password");
        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, upgradedResult);
    }
}
