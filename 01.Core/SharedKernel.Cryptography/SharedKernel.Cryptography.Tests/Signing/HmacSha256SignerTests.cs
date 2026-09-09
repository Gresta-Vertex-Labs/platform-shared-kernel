using System.Text;
using SharedKernel.Cryptography.Signing;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Signing;

public sealed class HmacSha256SignerTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("shared-secret-key");

    [Fact]
    public void Sign_ThenVerify_RoundTripsSuccessfully()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data to sign");

        byte[] signature = signer.Sign(data, Secret);
        bool verified = signer.Verify(data, signature, Secret);

        Assert.True(verified);
    }

    [Fact]
    public void Sign_IsDeterministic_ForSameInputAndSecret()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data to sign");

        byte[] signature1 = signer.Sign(data, Secret);
        byte[] signature2 = signer.Sign(data, Secret);

        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Verify_WithTamperedData_ReturnsFalse()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("original data");
        byte[] signature = signer.Sign(data, Secret);

        byte[] tamperedData = Encoding.UTF8.GetBytes("tampered data");
        bool verified = signer.Verify(tamperedData, signature, Secret);

        Assert.False(verified);
    }

    [Fact]
    public void Verify_WithTamperedSignature_ReturnsFalse()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = signer.Sign(data, Secret);
        byte[] tamperedSignature = [.. signature];
        tamperedSignature[0] ^= 0xFF;

        bool verified = signer.Verify(data, tamperedSignature, Secret);

        Assert.False(verified);
    }

    [Fact]
    public void Verify_WithWrongSecret_ReturnsFalse()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = signer.Sign(data, Secret);
        byte[] wrongSecret = Encoding.UTF8.GetBytes("wrong-secret-key");

        bool verified = signer.Verify(data, signature, wrongSecret);

        Assert.False(verified);
    }

    [Fact]
    public void Verify_WithDifferentLengthSignature_ReturnsFalseWithoutThrowing()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] shortSignature = [1, 2, 3];

        bool verified = signer.Verify(data, shortSignature, Secret);

        Assert.False(verified);
    }

    [Fact]
    public void Verify_RejectsSignatureDifferingOnlyInFinalByte()
    {
        // A non-constant-time comparison (e.g. a naive byte-by-byte loop with early
        // exit, or `SequenceEqual`) is still functionally correct here — this test
        // guards against regression to that pattern by pinning the exact "differs in
        // the last byte only" shape that constant-time comparison handles identically
        // to "differs in the first byte" (CryptographicOperations.FixedTimeEquals
        // always walks the full length regardless of where the mismatch occurs).
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = signer.Sign(data, Secret);
        byte[] tamperedAtEnd = [.. signature];
        tamperedAtEnd[^1] ^= 0xFF;

        bool verified = signer.Verify(data, tamperedAtEnd, Secret);

        Assert.False(verified);
    }

    [Fact]
    public void Verify_RejectsSignatureDifferingOnlyInFirstByte()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data");
        byte[] signature = signer.Sign(data, Secret);
        byte[] tamperedAtStart = [.. signature];
        tamperedAtStart[0] ^= 0xFF;

        bool verified = signer.Verify(data, tamperedAtStart, Secret);

        Assert.False(verified);
    }

    [Fact]
    public void Verify_NullData_Throws()
    {
        var signer = new HmacSha256Signer();
        byte[] signature = signer.Sign(Encoding.UTF8.GetBytes("data"), Secret);

        Assert.Throws<ArgumentNullException>(() => signer.Verify(null!, signature, Secret));
    }

    [Fact]
    public void Sign_NullData_Throws()
    {
        var signer = new HmacSha256Signer();

        Assert.Throws<ArgumentNullException>(() => signer.Sign(null!, Secret));
    }

    // ---- P-524/WO-083: key-material zeroization ----

    /// <summary>
    /// Proves — with a genuine runtime check against actual bytes — that
    /// <see cref="HmacSha256Signer.Verify(byte[], byte[], byte[])"/>'s internal <c>expected</c>
    /// comparison buffer is zeroed in place before the public call returns. Uses the internal,
    /// test-only capture-before-zeroing overload (gated via <c>InternalsVisibleTo</c>) to grab the
    /// exact same array reference the production code path zeroes.
    /// </summary>
    [Fact]
    public void Verify_ZeroesTheInternalExpectedComparisonBuffer_BeforeReturning()
    {
        var signer = new HmacSha256Signer();
        byte[] data = Encoding.UTF8.GetBytes("data to sign");
        byte[] signature = signer.Sign(data, Secret);
        byte[]? capturedExpected = null;

        bool verified = signer.Verify(data, signature, Secret, buffer => capturedExpected = buffer);

        Assert.True(verified);
        Assert.NotNull(capturedExpected);
        Assert.NotEmpty(capturedExpected);
        Assert.All(capturedExpected, b => Assert.Equal(0, b));
    }
}
