using System.Text;
using SharedKernel.Cryptography.Hashing;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class Sha256ContentHasherTests
{
    private static byte[] SampleContent(string text = "The quick brown fox jumps over the lazy dog") =>
        Encoding.UTF8.GetBytes(text);

    [Fact]
    public void ComputeHash_ByteArray_IsDeterministicForIdenticalInput()
    {
        var hasher = new Sha256ContentHasher();
        byte[] content = SampleContent();

        byte[] first = hasher.ComputeHash(content);
        byte[] second = hasher.ComputeHash(content);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHash_ByteArray_ProducesThirtyTwoByteDigest()
    {
        var hasher = new Sha256ContentHasher();

        byte[] digest = hasher.ComputeHash(SampleContent());

        Assert.Equal(32, digest.Length);
    }

    [Fact]
    public void ComputeHash_SingleByteChange_ProducesDifferentDigest()
    {
        var hasher = new Sha256ContentHasher();
        byte[] original = SampleContent("The quick brown fox jumps over the lazy dog");
        byte[] mutated = SampleContent("The quick brown fox jumps over the lazy dot");

        byte[] originalDigest = hasher.ComputeHash(original);
        byte[] mutatedDigest = hasher.ComputeHash(mutated);

        Assert.NotEqual(originalDigest, mutatedDigest);
    }

    [Fact]
    public void ComputeHash_Stream_MatchesByteArrayOverloadForSameContent()
    {
        var hasher = new Sha256ContentHasher();
        byte[] content = SampleContent();

        byte[] fromBytes = hasher.ComputeHash(content);
        using var stream = new MemoryStream(content);
        byte[] fromStream = hasher.ComputeHash(stream);

        Assert.Equal(fromBytes, fromStream);
    }

    [Fact]
    public async Task ComputeHashAsync_Stream_MatchesByteArrayOverloadForSameContent()
    {
        var hasher = new Sha256ContentHasher();
        byte[] content = SampleContent();

        byte[] fromBytes = hasher.ComputeHash(content);
        using var stream = new MemoryStream(content);
        byte[] fromStreamAsync = await hasher.ComputeHashAsync(stream);

        Assert.Equal(fromBytes, fromStreamAsync);
    }

    [Fact]
    public async Task ComputeHashAsync_RespectsCancellation()
    {
        var hasher = new Sha256ContentHasher();
        using var stream = new MemoryStream(SampleContent());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => hasher.ComputeHashAsync(stream, cts.Token).AsTask());
    }

    [Fact]
    public void ComputeHash_NullByteArray_Throws()
    {
        var hasher = new Sha256ContentHasher();

        Assert.Throws<ArgumentNullException>(() => hasher.ComputeHash((byte[])null!));
    }

    [Fact]
    public void ComputeHash_NullStream_Throws()
    {
        var hasher = new Sha256ContentHasher();

        Assert.Throws<ArgumentNullException>(() => hasher.ComputeHash((Stream)null!));
    }

    [Fact]
    public async Task ComputeHashAsync_NullStream_Throws()
    {
        var hasher = new Sha256ContentHasher();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => hasher.ComputeHashAsync(null!).AsTask());
    }

    [Fact]
    public void ComputeHashHex_ProducesLowercaseHexEncodingOfDigest()
    {
        var hasher = new Sha256ContentHasher();
        byte[] content = SampleContent();

        string hex = hasher.ComputeHashHex(content);

        byte[] digest = hasher.ComputeHash(content);
        Assert.Equal(Convert.ToHexStringLower(digest), hex);
        Assert.Equal(hex, hex.ToLowerInvariant());
    }

    [Fact]
    public void ComputeHashBase64_ProducesBase64EncodingOfDigest()
    {
        var hasher = new Sha256ContentHasher();
        byte[] content = SampleContent();

        string base64 = hasher.ComputeHashBase64(content);

        byte[] digest = hasher.ComputeHash(content);
        Assert.Equal(Convert.ToBase64String(digest), base64);
    }

    [Fact]
    public void KnownAnswerTest_EmptyInput_MatchesWellKnownSha256Digest()
    {
        // SHA-256 of the empty byte array is a well-known constant — a strong sanity check
        // that this type is genuinely delegating to SHA-256 and not some other algorithm.
        var hasher = new Sha256ContentHasher();

        string hex = hasher.ComputeHashHex([]);

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            hex);
    }
}
