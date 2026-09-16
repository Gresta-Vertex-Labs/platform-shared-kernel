using SharedKernel.Cryptography.Signing;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeHmacSigner"/> against <c>IHmacSigner</c>'s contract: real HMAC-SHA256, the production
/// 32-byte minimum key length, and a record of every call.
/// </summary>
public sealed class FakeHmacSignerTests
{
    private static readonly byte[] Data = "payload"u8.ToArray();
    private static readonly byte[] Key = "0123456789abcdef0123456789abcdef"u8.ToArray();
    private static readonly byte[] OtherKey = "fedcba9876543210fedcba9876543210"u8.ToArray();

    [Fact]
    public void Sign_ThenVerify_RoundTrips()
    {
        var signer = new FakeHmacSigner();

        byte[] signature = signer.Sign(Data, Key);

        Assert.True(signer.Verify(Data, signature, Key));
    }

    [Fact]
    public void Sign_MatchesTheProductionSigner()
    {
        Assert.Equal(new HmacSha256Signer().Sign(Data, Key), new FakeHmacSigner().Sign(Data, Key));
    }

    [Fact]
    public void Verify_TamperedData_ReturnsFalse()
    {
        var signer = new FakeHmacSigner();
        byte[] signature = signer.Sign(Data, Key);

        Assert.False(signer.Verify("tampered"u8, signature, Key));
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        var signer = new FakeHmacSigner();
        byte[] signature = signer.Sign(Data, Key);

        Assert.False(signer.Verify(Data, signature, OtherKey));
    }

    [Fact]
    public void Verify_SameLengthSignatureWithOneByteChanged_ReturnsFalse()
    {
        var signer = new FakeHmacSigner();
        byte[] signature = signer.Sign(Data, Key);
        byte[] changed = (byte[])signature.Clone();
        changed[0] ^= 0xFF;

        Assert.False(signer.Verify(Data, changed, Key));
    }

    [Fact]
    public void Verify_TruncatedSignature_ReturnsFalse()
    {
        var signer = new FakeHmacSigner();
        byte[] signature = signer.Sign(Data, Key);

        Assert.False(signer.Verify(Data, signature.AsSpan(0, 16), Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(HmacSha256Signer.MinimumKeyLength - 1)]
    public void ShortKey_Throws_OnSignAndVerify(int keyLength)
    {
        var signer = new FakeHmacSigner();
        byte[] shortKey = new byte[keyLength];
        byte[] signature = signer.Sign(Data, Key);

        Assert.Throws<ArgumentException>(() => signer.Sign(Data, shortKey));
        Assert.Throws<ArgumentException>(() => signer.Verify(Data, signature, shortKey));
    }

    [Fact]
    public void MinimumLengthKey_IsAccepted()
    {
        var signer = new FakeHmacSigner();
        byte[] key = new byte[HmacSha256Signer.MinimumKeyLength];

        Assert.True(signer.Verify(Data, signer.Sign(Data, key), key));
    }

    [Fact]
    public void SignedPayloads_AndVerifiedPayloads_RecordEveryCall()
    {
        var signer = new FakeHmacSigner();
        byte[] signature = signer.Sign(Data, Key);

        signer.Verify(Data, signature, Key);
        signer.Verify(Data, signature, OtherKey);

        Assert.Single(signer.SignedPayloads);
        Assert.Equal(Data, signer.SignedPayloads[0].Data);
        Assert.Equal(Key, signer.SignedPayloads[0].Key);
        Assert.Equal(2, signer.VerifiedPayloads.Count);
        Assert.Equal(Key, signer.VerifiedPayloads[0].Key);
        Assert.Equal(OtherKey, signer.VerifiedPayloads[1].Key);
    }

    [Fact]
    public void RejectedShortKeyCall_IsNotRecorded()
    {
        var signer = new FakeHmacSigner();

        Assert.Throws<ArgumentException>(() => signer.Sign(Data, new byte[8]));

        Assert.Empty(signer.SignedPayloads);
    }
}
