using SharedKernel.Cryptography.Signing;
using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Validation;

/// <summary>
/// Proves <see cref="ConstantTimeKeyComparer"/> genuinely routes every comparison through
/// <see cref="IHmacSigner"/>'s constant-time <c>Verify</c> path rather than a raw
/// <c>string.Equals</c>/<c>==</c> early-exit short-circuit (WO-057, P-370, T-16).
/// </summary>
public sealed class ConstantTimeKeyComparerTests
{
    // ---- Correctness, with the real HMAC-SHA256 signer ----

    [Fact]
    public void AreEqual_WithRealSigner_ReturnsTrue_ForIdenticalStrings()
    {
        var signer = new HmacSha256Signer();

        var result = ConstantTimeKeyComparer.AreEqual(signer, "the-secret-key", "the-secret-key");

        Assert.True(result);
    }

    [Fact]
    public void AreEqual_WithRealSigner_ReturnsFalse_ForDifferentStrings_SameLength()
    {
        var signer = new HmacSha256Signer();

        var result = ConstantTimeKeyComparer.AreEqual(signer, "key-value-one", "key-value-two");

        Assert.False(result);
    }

    [Fact]
    public void AreEqual_WithRealSigner_ReturnsFalse_ForDifferentStrings_DifferentLength()
    {
        var signer = new HmacSha256Signer();

        var result = ConstantTimeKeyComparer.AreEqual(signer, "short", "a-much-longer-value");

        Assert.False(result);
    }

    [Fact]
    public void AreEqual_WithRealSigner_ReturnsFalse_WhenOnlyFirstCharacterDiffers()
    {
        // A naive early-exit string.Equals/== would still return false here — this alone does not
        // prove constant-time behavior. Paired with the "differs only in the last character" case
        // below and the signer-delegation proof further down, it demonstrates the comparison is not
        // implemented as a position-sensitive shortcut anywhere in THIS class.
        var signer = new HmacSha256Signer();

        var result = ConstantTimeKeyComparer.AreEqual(signer, "Xabcdefghij", "Yabcdefghij");

        Assert.False(result);
    }

    [Fact]
    public void AreEqual_WithRealSigner_ReturnsFalse_WhenOnlyLastCharacterDiffers()
    {
        var signer = new HmacSha256Signer();

        var result = ConstantTimeKeyComparer.AreEqual(signer, "abcdefghijX", "abcdefghijY");

        Assert.False(result);
    }

    // ---- Argument validation ----

    [Fact]
    public void AreEqual_NullSigner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ConstantTimeKeyComparer.AreEqual(null!, "a", "b"));
    }

    [Fact]
    public void AreEqual_NullLeft_ThrowsArgumentNullException()
    {
        var signer = new HmacSha256Signer();

        Assert.Throws<ArgumentNullException>(() => ConstantTimeKeyComparer.AreEqual(signer, null!, "b"));
    }

    [Fact]
    public void AreEqual_NullRight_ThrowsArgumentNullException()
    {
        var signer = new HmacSha256Signer();

        Assert.Throws<ArgumentNullException>(() => ConstantTimeKeyComparer.AreEqual(signer, "a", null!));
    }

    // ---- Proving the constant-time compare path is actually invoked (no early-exit shortcut) ----

    [Fact]
    public void AreEqual_TrustsHmacSignerVerifyOutcome_EvenForTwoDifferentStrings()
    {
        // Two different strings — a naive string.Equals/== early-exit check would return false
        // before ever consulting a signer. Forcing the injected IHmacSigner.Verify to report a match
        // anyway and observing AreEqual still returns true proves the method has NO independent
        // string-comparison path of its own: its only source of truth is the constant-time
        // IHmacSigner.Verify outcome (which internally uses CryptographicOperations.FixedTimeEquals).
        var signer = new RecordingHmacSigner(signature: [9, 9, 9], verifyResult: true);

        var result = ConstantTimeKeyComparer.AreEqual(signer, "totally-different-left", "utterly-different-right");

        Assert.True(result);
        Assert.Equal(1, signer.SignCallCount);
        Assert.Equal(1, signer.VerifyCallCount);
        Assert.Equal("totally-different-left", signer.LastSignedData);
        Assert.Equal("utterly-different-right", signer.LastVerifiedData);
    }

    [Fact]
    public void AreEqual_TrustsHmacSignerVerifyOutcome_EvenForTwoIdenticalStrings()
    {
        // The mirror image of the case above: two IDENTICAL strings, but the injected signer's
        // Verify reports no match. AreEqual must return false — proving it never short-circuits to
        // "true" via a raw comparison of the (here, equal) input strings either.
        var signer = new RecordingHmacSigner(signature: [1], verifyResult: false);

        var result = ConstantTimeKeyComparer.AreEqual(signer, "same-value", "same-value");

        Assert.False(result);
        Assert.Equal(1, signer.SignCallCount);
        Assert.Equal(1, signer.VerifyCallCount);
    }

    [Fact]
    public void AreEqual_SignsTheLeftValue_ThenVerifiesTheRightValueAgainstThatSignature()
    {
        var signer = new RecordingHmacSigner(signature: [4, 2], verifyResult: true);

        ConstantTimeKeyComparer.AreEqual(signer, "left-value", "right-value");

        Assert.Equal("left-value", signer.LastSignedData);
        Assert.Equal("right-value", signer.LastVerifiedData);
        Assert.Equal(signer.LastSignature, signer.LastVerifiedSignature);
    }

    /// <summary>
    /// A hand-rolled <see cref="IHmacSigner"/> double recording every call — used instead of a
    /// mocking framework since this test project carries no such dependency (mirrors
    /// <c>ApiKeyAuthenticationHandlerTests</c>'s own <c>StubApiKeyValidator</c> pattern).
    /// </summary>
    private sealed class RecordingHmacSigner(byte[] signature, bool verifyResult) : IHmacSigner
    {
        public int SignCallCount { get; private set; }

        public int VerifyCallCount { get; private set; }

        public string? LastSignedData { get; private set; }

        public string? LastVerifiedData { get; private set; }

        public byte[]? LastSignature { get; private set; }

        public byte[]? LastVerifiedSignature { get; private set; }

        public byte[] Sign(byte[] data, byte[] secret)
        {
            SignCallCount++;
            LastSignedData = System.Text.Encoding.UTF8.GetString(data);
            LastSignature = signature;
            return signature;
        }

        public bool Verify(byte[] data, byte[] signature, byte[] secret)
        {
            VerifyCallCount++;
            LastVerifiedData = System.Text.Encoding.UTF8.GetString(data);
            LastVerifiedSignature = signature;
            return verifyResult;
        }
    }
}
