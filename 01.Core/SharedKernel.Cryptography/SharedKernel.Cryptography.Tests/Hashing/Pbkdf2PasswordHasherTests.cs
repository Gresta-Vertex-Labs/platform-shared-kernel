using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class Pbkdf2PasswordHasherTests
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
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));

        string hash = hasher.Hash("correct-password");
        PasswordVerificationResult result = hasher.Verify(hash, "correct-password");

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFailed()
    {
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));

        string hash = hasher.Hash("correct-password");
        PasswordVerificationResult result = hasher.Verify(hash, "wrong-password");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_WithMalformedHash_ReturnsFailed()
    {
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));

        PasswordVerificationResult result = hasher.Verify("not-a-valid-hash", "any-password");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void Hash_ProducesDifferentOutputForSamePasswordAcrossCalls()
    {
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));

        string hash1 = hasher.Hash("same-password");
        string hash2 = hasher.Hash("same-password");

        Assert.NotEqual(hash1, hash2); // different random salt each time
    }

    [Fact]
    public void Verify_WhenIterationCountIncreasedSinceHashing_ReturnsSuccessRehashNeeded()
    {
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));
        string hash = hasher.Hash("correct-password");

        var upgradedHasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(2000));
        PasswordVerificationResult result = upgradedHasher.Verify(hash, "correct-password");

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void Verify_WhenIterationCountUnchanged_ReturnsSuccessNotRehashNeeded()
    {
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));
        string hash = hasher.Hash("correct-password");

        PasswordVerificationResult result = hasher.Verify(hash, "correct-password");

        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void Hash_NullPassword_Throws()
    {
        var hasher = new Pbkdf2PasswordHasher(CreateOptionsMonitor(1000));

        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Pbkdf2PasswordHasher(null!));
    }
}
