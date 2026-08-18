using SharedKernel.Cryptography.Signing;
using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

/// <summary>
/// Proves <see cref="ApiKeyRotationComparer.AnyMatch(string, IReadOnlyList{string})"/> always evaluates
/// every candidate — never short-circuiting on an early match — so elapsed comparison time never
/// correlates with which, or how many, candidates matched (WO-060, P-389, T-38/T-39). Mirrors
/// <c>ConstantTimeKeyComparerTests</c>'s verification technique: a hand-rolled <see cref="IHmacSigner"/>
/// double recording every invocation, reached via the package-internal
/// <see cref="ApiKeyRotationComparer.AnyMatch(string, IReadOnlyList{string}, IHmacSigner)"/> testability
/// seam and this test project's existing <c>InternalsVisibleTo</c> grant.
/// </summary>
public sealed class ApiKeyRotationComparisonTests
{
    // ---- Correctness, with the real HMAC-SHA256 signer ----

    [Fact]
    public void AnyMatch_WithRealSigner_ReturnsTrue_WhenPresentedMatchesOneCandidate()
    {
        var result = ApiKeyRotationComparer.AnyMatch("key-b", ["key-a", "key-b", "key-c"]);

        Assert.True(result);
    }

    [Fact]
    public void AnyMatch_WithRealSigner_ReturnsFalse_WhenPresentedMatchesNoCandidate()
    {
        var result = ApiKeyRotationComparer.AnyMatch("key-z", ["key-a", "key-b", "key-c"]);

        Assert.False(result);
    }

    [Fact]
    public void AnyMatch_WithRealSigner_ReturnsFalse_ForEmptyCandidateList()
    {
        var result = ApiKeyRotationComparer.AnyMatch("key-a", []);

        Assert.False(result);
    }

    // ---- Argument validation ----

    [Fact]
    public void AnyMatch_NullPresented_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ApiKeyRotationComparer.AnyMatch(null!, ["key-a"]));
    }

    [Fact]
    public void AnyMatch_NullCandidates_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ApiKeyRotationComparer.AnyMatch("key-a", null!));
    }

    // ---- T-38: proving every candidate is always compared, never short-circuited ----

    [Fact]
    public void AnyMatch_MatchOnFirstCandidate_StillComparesEveryRemainingCandidate()
    {
        // The case a short-circuiting implementation would optimize away: the match is the very FIRST
        // candidate, so a naive "return true on first match" implementation would compare only once.
        var signer = new RecordingHmacSigner(verifyResult: true);

        var result = ApiKeyRotationComparer.AnyMatch("presented", ["match-1", "candidate-2", "candidate-3", "candidate-4"], signer);

        Assert.True(result);
        Assert.Equal(4, signer.VerifyCallCount);
    }

    [Fact]
    public void AnyMatch_MatchOnLastCandidate_ComparesEveryCandidate()
    {
        var signer = new RecordingHmacSigner(verifyResult: false, matchOnCallNumber: 5);

        var result = ApiKeyRotationComparer.AnyMatch("presented", ["c1", "c2", "c3", "c4", "match-5"], signer);

        Assert.True(result);
        Assert.Equal(5, signer.VerifyCallCount);
    }

    [Fact]
    public void AnyMatch_NoMatch_ComparesEveryCandidate()
    {
        var signer = new RecordingHmacSigner(verifyResult: false);

        var result = ApiKeyRotationComparer.AnyMatch("presented", ["c1", "c2", "c3", "c4", "c5"], signer);

        Assert.False(result);
        Assert.Equal(5, signer.VerifyCallCount);
    }

    [Fact]
    public void AnyMatch_MultipleMatches_StillComparesEveryCandidate()
    {
        // Every candidate happens to match — a buggy "stop at first match" implementation would still
        // only report one comparison; AnyMatch must report every one regardless.
        var signer = new RecordingHmacSigner(verifyResult: true);

        var result = ApiKeyRotationComparer.AnyMatch("presented", ["c1", "c2", "c3"], signer);

        Assert.True(result);
        Assert.Equal(3, signer.VerifyCallCount);
    }

    // ---- T-39: rotation-window scenario — old key still valid, new key valid, both accepted ----

    [Fact]
    public void RotationWindow_OldKeyStillValid_MatchesAgainstCandidateSet()
    {
        const string oldKey = "old-key-still-in-grace-window";
        const string newKey = "new-rotated-key";
        IReadOnlyList<string> activeCandidates = [newKey, oldKey];

        Assert.True(ApiKeyRotationComparer.AnyMatch(oldKey, activeCandidates));
    }

    [Fact]
    public void RotationWindow_NewKeyValid_MatchesAgainstCandidateSet()
    {
        const string oldKey = "old-key-still-in-grace-window";
        const string newKey = "new-rotated-key";
        IReadOnlyList<string> activeCandidates = [newKey, oldKey];

        Assert.True(ApiKeyRotationComparer.AnyMatch(newKey, activeCandidates));
    }

    [Fact]
    public void RotationWindow_RevokedKey_NeitherOldNorNewMatches()
    {
        const string oldKey = "old-key-still-in-grace-window";
        const string newKey = "new-rotated-key";
        const string revokedKey = "a-fully-revoked-key-from-before-the-window";
        IReadOnlyList<string> activeCandidates = [newKey, oldKey];

        Assert.False(ApiKeyRotationComparer.AnyMatch(revokedKey, activeCandidates));
    }

    /// <summary>
    /// A hand-rolled <see cref="IHmacSigner"/> double counting every <c>Verify</c> invocation — mirrors
    /// <c>ConstantTimeKeyComparerTests.RecordingHmacSigner</c>. <c>Sign</c> always returns a fixed
    /// signature; <c>Verify</c> reports a match either unconditionally (<paramref name="verifyResult"/>)
    /// or only on a specific call number (<paramref name="matchOnCallNumber"/>), letting a test place the
    /// single matching candidate at any position in the list.
    /// </summary>
    private sealed class RecordingHmacSigner(bool verifyResult = false, int? matchOnCallNumber = null) : IHmacSigner
    {
        private static readonly byte[] FixedSignature = [1, 2, 3];

        public int SignCallCount { get; private set; }

        public int VerifyCallCount { get; private set; }

        public byte[] Sign(byte[] data, byte[] secret)
        {
            SignCallCount++;
            return FixedSignature;
        }

        public bool Verify(byte[] data, byte[] signature, byte[] secret)
        {
            VerifyCallCount++;
            return matchOnCallNumber is { } n ? VerifyCallCount == n : verifyResult;
        }
    }
}
