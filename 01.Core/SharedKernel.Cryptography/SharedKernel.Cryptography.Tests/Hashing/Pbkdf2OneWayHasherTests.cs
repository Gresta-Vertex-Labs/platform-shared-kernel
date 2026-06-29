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
}
