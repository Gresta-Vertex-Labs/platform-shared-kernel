using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeContentHasher"/> against <c>IContentHasher</c>'s documented contract.
/// Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-57.
/// </summary>
public sealed class FakeContentHasherTests
{
    [Fact]
    public void ComputeHash_ByteArray_IsDeterministic_ForIdenticalInput()
    {
        var hasher = new FakeContentHasher();
        var content = "identical content"u8.ToArray();

        var first = hasher.ComputeHash(content);
        var second = hasher.ComputeHash((byte[])content.Clone());

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHash_SingleByteChange_ProducesADifferentDigest()
    {
        var hasher = new FakeContentHasher();
        var original = "content"u8.ToArray();
        var changed = (byte[])original.Clone();
        changed[0] ^= 0x01;

        var originalHash = hasher.ComputeHash(original);
        var changedHash = hasher.ComputeHash(changed);

        Assert.NotEqual(originalHash, changedHash);
    }

    [Fact]
    public void ComputeHash_Stream_IsByteIdentical_ToComputeHash_ByteArray()
    {
        var hasher = new FakeContentHasher();
        var content = "stream content"u8.ToArray();

        var fromBytes = hasher.ComputeHash(content);
        var fromStream = hasher.ComputeHash(new MemoryStream(content));

        Assert.Equal(fromBytes, fromStream);
    }

    [Fact]
    public async Task ComputeHashAsync_IsByteIdentical_ToComputeHash_ByteArray()
    {
        var hasher = new FakeContentHasher();
        var content = "async stream content"u8.ToArray();

        var fromBytes = hasher.ComputeHash(content);
        var fromAsyncStream = await hasher.ComputeHashAsync(new MemoryStream(content));

        Assert.Equal(fromBytes, fromAsyncStream);
    }

    [Fact]
    public void HashedContent_RecordsEveryPayload_AcrossByteArrayAndSyncStreamOverloads()
    {
        var hasher = new FakeContentHasher();
        var content = "a"u8.ToArray();

        hasher.ComputeHash(content);
        hasher.ComputeHash(new MemoryStream(content));

        Assert.Equal(2, hasher.HashedContent.Count);
        Assert.All(hasher.HashedContent, recorded => Assert.Equal(content, recorded));
    }

    [Fact]
    public async Task HashedContent_RecordsAsyncStreamPayloadToo()
    {
        var hasher = new FakeContentHasher();
        var content = "async"u8.ToArray();

        await hasher.ComputeHashAsync(new MemoryStream(content));

        Assert.Single(hasher.HashedContent);
        Assert.Equal(content, hasher.HashedContent[0]);
    }

    [Fact]
    public void ComputeHash_NullByteArray_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeContentHasher().ComputeHash((byte[])null!));

    [Fact]
    public void ComputeHash_NullStream_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeContentHasher().ComputeHash((Stream)null!));

    [Fact]
    public async Task ComputeHashAsync_NullStream_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => new FakeContentHasher().ComputeHashAsync(null!).AsTask());
}
