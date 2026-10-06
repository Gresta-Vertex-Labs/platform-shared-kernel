using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeOneWayHasher"/> against <c>IOneWayHasher</c>'s contract: the production PHC format and
/// verification rules, at an iteration count low enough for test suites.
/// </summary>
public sealed class FakeOneWayHasherTests
{
    [Fact]
    public void Hash_ThenVerify_WithCorrectSecret_ReturnsSuccess()
    {
        var hasher = new FakeOneWayHasher();

        string hash = hasher.Hash("correct-secret");

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "correct-secret"));
    }

    [Fact]
    public void Verify_WithWrongSecret_ReturnsFailed()
    {
        var hasher = new FakeOneWayHasher();
        string hash = hasher.Hash("correct-secret");

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "wrong-secret"));
    }

    [Fact]
    public void Hash_WritesAPbkdf2PhcStringWithTheFakeIterationCount()
    {
        var hasher = new FakeOneWayHasher();

        string hash = hasher.Hash("secret");

        Assert.StartsWith($"${Pbkdf2OneWayHashAlgorithm.Id}$i={hasher.Iterations}$", hash, StringComparison.Ordinal);
        Assert.True(PhcHashString.TryParse(hash, out PhcHashString? parsed));
        Assert.Equal(Pbkdf2OneWayHashAlgorithm.Id, parsed!.AlgorithmId);
    }

    [Fact]
    public void Hash_SameSecretTwice_UsesAFreshSaltEachTime()
    {
        var hasher = new FakeOneWayHasher();

        string first = hasher.Hash("secret");
        string second = hasher.Hash("secret");

        Assert.NotEqual(first, second);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(first, "secret"));
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(second, "secret"));
    }

    [Fact]
    public void Verify_AfterIterationsChange_ReturnsSuccessRehashNeeded()
    {
        var hasher = new FakeOneWayHasher { Iterations = 4 };
        string hash = hasher.Hash("secret");

        hasher.Iterations = 8;

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(hash, "secret"));
    }

    [Fact]
    public void Verify_HashFromAnotherHasherInstance_Succeeds()
    {
        string hash = new FakeOneWayHasher().Hash("secret");

        Assert.Equal(HashVerificationResult.Success, new FakeOneWayHasher().Verify(hash, "secret"));
    }

    [Fact]
    public void Verify_NormalizesUnicodeToNfkc()
    {
        var hasher = new FakeOneWayHasher();

        // U+FB01 (the "fi" ligature) normalizes to "fi" under NFKC.
        string hash = hasher.Hash("pro" + (char)0xFB01 + "le");

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "profile"));
    }

    [Theory]
    [InlineData("not-a-hash!!!")]
    [InlineData("$pbkdf2-sha256$i=16$not-base64$also-not")]
    [InlineData("$unknown-algorithm$i=16$c2FsdHNhbHRzYWx0c2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("")]
    public void Verify_MalformedOrUnknownHash_ReturnsFailed(string hash) =>
        Assert.Equal(HashVerificationResult.Failed, new FakeOneWayHasher().Verify(hash, "secret"));

    [Fact]
    public void Verify_IterationCountAboveTheProductionCeiling_ReturnsFailed()
    {
        var hasher = new FakeOneWayHasher();
        string hash = hasher.Hash("secret").Replace(
            $"i={hasher.Iterations}",
            $"i={Pbkdf2Options.MaximumIterations + 1}",
            StringComparison.Ordinal);

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "secret"));
    }

    [Fact]
    public void Verify_EmptySecret_ReturnsFailed()
    {
        var hasher = new FakeOneWayHasher();
        string hash = hasher.Hash("secret");

        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, string.Empty));
    }

    [Fact]
    public void Hash_EmptySecret_Throws() =>
        Assert.Throws<ArgumentException>(() => new FakeOneWayHasher().Hash(string.Empty));

    [Fact]
    public void Hash_NullSecret_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeOneWayHasher().Hash(null!));

    [Fact]
    public void Verify_NullArguments_Throw()
    {
        var hasher = new FakeOneWayHasher();

        Assert.Throws<ArgumentNullException>(() => hasher.Verify(null!, "secret"));
        Assert.Throws<ArgumentNullException>(() => hasher.Verify("hash", null!));
    }

    [Fact]
    public void Iterations_DefaultsToAValueFarBelowTheProductionMinimum()
    {
        var hasher = new FakeOneWayHasher();

        Assert.Equal(16, hasher.Iterations);
        Assert.True(hasher.Iterations < Pbkdf2Options.MinimumIterations);
    }
}
