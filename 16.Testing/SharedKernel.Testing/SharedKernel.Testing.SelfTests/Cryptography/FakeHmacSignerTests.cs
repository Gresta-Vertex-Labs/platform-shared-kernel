using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeHmacSigner"/> against <c>IHmacSigner</c>'s documented contract. Proven
/// exclusively in <c>SharedKernel.Testing.SelfTests</c> — see <c>16.Testing/state-map.md</c> T-56.
/// </summary>
public sealed class FakeHmacSignerTests
{
    [Fact]
    public void Sign_ThenVerify_RoundTrips()
    {
        var signer = new FakeHmacSigner();
        var data = "payload"u8.ToArray();
        var secret = "shared-secret"u8.ToArray();

        var signature = signer.Sign(data, secret);

        Assert.True(signer.Verify(data, signature, secret));
    }

    [Fact]
    public void Verify_TamperedData_ReturnsFalse()
    {
        var signer = new FakeHmacSigner();
        var secret = "shared-secret"u8.ToArray();
        var signature = signer.Sign("payload"u8.ToArray(), secret);

        Assert.False(signer.Verify("tampered"u8.ToArray(), signature, secret));
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var signer = new FakeHmacSigner();
        var data = "payload"u8.ToArray();
        var signature = signer.Sign(data, "secret-a"u8.ToArray());

        Assert.False(signer.Verify(data, signature, "secret-b"u8.ToArray()));
    }

    [Fact]
    public void Verify_ConstantTimeCompare_CorrectlyDistinguishesSameLengthMismatch()
    {
        // Timing itself can't be observed in a unit test, but the constant-time compare
        // (CryptographicOperations.FixedTimeEquals) must still be functionally correct: a full
        // match succeeds and a same-length-but-differing-content signature fails.
        var signer = new FakeHmacSigner();
        var data = "payload"u8.ToArray();
        var secret = "secret"u8.ToArray();
        var signature = signer.Sign(data, secret);
        var sameLengthWrongSignature = (byte[])signature.Clone();
        sameLengthWrongSignature[0] ^= 0xFF;

        Assert.True(signer.Verify(data, signature, secret));
        Assert.False(signer.Verify(data, sameLengthWrongSignature, secret));
    }

    [Fact]
    public void SignedPayloads_AndVerifiedPayloads_RecordEveryCall()
    {
        var signer = new FakeHmacSigner();
        var data = "payload"u8.ToArray();
        var secret = "secret"u8.ToArray();
        var signature = signer.Sign(data, secret);

        signer.Verify(data, signature, secret);

        Assert.Single(signer.SignedPayloads);
        Assert.Single(signer.VerifiedPayloads);
        Assert.Equal(data, signer.SignedPayloads[0].Data);
        Assert.Equal(secret, signer.SignedPayloads[0].Secret);
        Assert.Equal(data, signer.VerifiedPayloads[0].Data);
        Assert.Equal(secret, signer.VerifiedPayloads[0].Secret);
    }

    [Fact]
    public void Sign_NullArguments_Throw()
    {
        var signer = new FakeHmacSigner();

        Assert.Throws<ArgumentNullException>(() => signer.Sign(null!, "secret"u8.ToArray()));
        Assert.Throws<ArgumentNullException>(() => signer.Sign("data"u8.ToArray(), null!));
    }

    [Fact]
    public void Verify_NullArguments_Throw()
    {
        var signer = new FakeHmacSigner();
        var signature = signer.Sign("data"u8.ToArray(), "secret"u8.ToArray());

        Assert.Throws<ArgumentNullException>(() => signer.Verify(null!, signature, "secret"u8.ToArray()));
        Assert.Throws<ArgumentNullException>(() => signer.Verify("data"u8.ToArray(), null!, "secret"u8.ToArray()));
        Assert.Throws<ArgumentNullException>(() => signer.Verify("data"u8.ToArray(), signature, null!));
    }
}
