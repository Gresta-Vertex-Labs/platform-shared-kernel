using System.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeContentHasher"/> against <c>IContentHasher</c>'s contract: real SHA-256 digests from every
/// overload, and a record of every hashed buffer.
/// </summary>
public sealed class FakeContentHasherTests
{
    [Fact]
    public void ComputeHash_MatchesSha256()
    {
        var hasher = new FakeContentHasher();
        byte[] content = "identical content"u8.ToArray();

        Assert.Equal(SHA256.HashData(content), hasher.ComputeHash(content));
        Assert.Equal(new Sha256ContentHasher().ComputeHash(content), hasher.ComputeHash(content));
    }

    [Fact]
    public void ComputeHash_SingleByteChange_ProducesADifferentDigest()
    {
        var hasher = new FakeContentHasher();
        byte[] original = "content"u8.ToArray();
        byte[] changed = (byte[])original.Clone();
        changed[0] ^= 0x01;

        Assert.NotEqual(hasher.ComputeHash(original), hasher.ComputeHash(changed));
    }

    [Fact]
    public void ComputeHash_EmptyContent_IsTheSha256OfNothing() =>
        Assert.Equal(SHA256.HashData([]), new FakeContentHasher().ComputeHash(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void ComputeHash_Stream_IsByteIdentical_ToComputeHash_Span()
    {
        var hasher = new FakeContentHasher();
        byte[] content = "stream content"u8.ToArray();

        Assert.Equal(hasher.ComputeHash(content), hasher.ComputeHash(new MemoryStream(content)));
    }

    [Fact]
    public async Task ComputeHashAsync_IsByteIdentical_ToComputeHash_Span()
    {
        var hasher = new FakeContentHasher();
        byte[] content = "async stream content"u8.ToArray();

        byte[] fromAsyncStream = await hasher.ComputeHashAsync(new MemoryStream(content));

        Assert.Equal(hasher.ComputeHash(content), fromAsyncStream);
    }

    [Fact]
    public void ComputeHashHex_Extension_UsesTheFake()
    {
        var hasher = new FakeContentHasher();
        byte[] content = "hex"u8.ToArray();

        string hex = hasher.ComputeHashHex(content);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), hex, ignoreCase: true);
        Assert.Single(hasher.HashedContent);
    }

    [Fact]
    public async Task HashedContent_RecordsEveryPayload_AcrossAllOverloads()
    {
        var hasher = new FakeContentHasher();
        byte[] content = "a"u8.ToArray();

        hasher.ComputeHash(content);
        hasher.ComputeHash(new MemoryStream(content));
        await hasher.ComputeHashAsync(new MemoryStream(content));

        Assert.Equal(3, hasher.HashedContent.Count);
        Assert.All(hasher.HashedContent, recorded => Assert.Equal(content, recorded));
    }

    [Fact]
    public void ComputeHash_NullStream_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeContentHasher().ComputeHash((Stream)null!));

    [Fact]
    public async Task ComputeHashAsync_NullStream_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => new FakeContentHasher().ComputeHashAsync(null!).AsTask());
}
