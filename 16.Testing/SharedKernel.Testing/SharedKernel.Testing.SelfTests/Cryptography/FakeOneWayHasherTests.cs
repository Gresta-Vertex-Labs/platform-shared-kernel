using SharedKernel.Cryptography.Hashing;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeOneWayHasher"/> against <c>IOneWayHasher</c>'s documented contract. No
/// consuming domain can reference <c>16.Testing</c> at all (<c>01.Core</c> sits below it and
/// references nothing), so this self-test is the only behavioral proof, mirroring
/// <c>Clocks/FakeClockTests.cs</c>'s routing rationale (see <c>16.Testing/state-map.md</c> T-56).
/// </summary>
public sealed class FakeOneWayHasherTests
{
    [Fact]
    public void Hash_ThenVerify_WithCorrectSecret_ReturnsSuccess()
    {
        var hasher = new FakeOneWayHasher();

        var hash = hasher.Hash("correct-secret");
        var result = hasher.Verify(hash, "correct-secret");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_WithWrongSecret_ReturnsFailed()
    {
        var hasher = new FakeOneWayHasher();
        var hash = hasher.Hash("correct-secret");

        var result = hasher.Verify(hash, "wrong-secret");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_AfterIterationsMutated_ReturnsSuccessRehashNeeded()
    {
        var hasher = new FakeOneWayHasher { Iterations = 4 };
        var hash = hasher.Hash("secret");

        hasher.Iterations = 8;
        var result = hasher.Verify(hash, "secret");

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void Verify_WithUnchangedIterations_ReturnsSuccess_NotRehashNeeded()
    {
        var hasher = new FakeOneWayHasher();
        var hash = hasher.Hash("secret");

        var result = hasher.Verify(hash, "secret");

        Assert.Equal(HashVerificationResult.Success, result);
    }

    [Fact]
    public void Verify_MalformedHash_NotValidBase64_ReturnsFailed()
    {
        var hasher = new FakeOneWayHasher();

        var result = hasher.Verify("not-a-valid-base64-hash!!!", "secret");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Verify_ValidBase64ButWrongFormatMarker_ReturnsFailed()
    {
        var hasher = new FakeOneWayHasher();
        var bogus = Convert.ToBase64String([0xFF, 1, 2, 3, 4, 5, 6, 7]);

        var result = hasher.Verify(bogus, "secret");

        Assert.Equal(HashVerificationResult.Failed, result);
    }

    [Fact]
    public void Hash_NullSecret_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeOneWayHasher().Hash(null!));

    [Fact]
    public void Verify_NullHash_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeOneWayHasher().Verify(null!, "secret"));

    [Fact]
    public void Verify_NullSecret_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeOneWayHasher().Verify("hash", null!));

    [Fact]
    public void Iterations_DefaultsToTinyValue_NeverTheRealProductionDefault()
    {
        var hasher = new FakeOneWayHasher();

        Assert.Equal(4, hasher.Iterations);
        Assert.NotEqual(600_000, hasher.Iterations);
    }
}
