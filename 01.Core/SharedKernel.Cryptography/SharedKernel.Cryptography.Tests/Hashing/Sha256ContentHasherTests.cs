using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class Sha256ContentHasherTests
{
    private const string AbcDigestHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private const string EmptyDigestHex = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly Sha256ContentHasher _hasher = new();

    [Theory]
    [InlineData("abc", AbcDigestHex)]
    [InlineData("", EmptyDigestHex)]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")]
    public void ComputeHash_Span_MatchesKnownVector(string input, string expectedHex)
    {
        byte[] digest = _hasher.ComputeHash(Encoding.ASCII.GetBytes(input));

        Assert.Equal(Convert.FromHexString(expectedHex), digest);
    }

    [Fact]
    public void ComputeHash_Stream_MatchesSpan()
    {
        byte[] content = RandomNumberGenerator.GetBytes(200_000);
        using var stream = new MemoryStream(content);

        Assert.Equal(_hasher.ComputeHash(content), _hasher.ComputeHash(stream));
    }

    [Fact]
    public async Task ComputeHashAsync_Stream_MatchesSpan()
    {
        byte[] content = RandomNumberGenerator.GetBytes(200_000);
        using var stream = new MemoryStream(content);

        byte[] digest = await _hasher.ComputeHashAsync(stream);

        Assert.Equal(_hasher.ComputeHash(content), digest);
    }

    [Fact]
    public async Task ComputeHashAsync_Stream_MatchesKnownVector()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        Assert.Equal(Convert.FromHexString(AbcDigestHex), await _hasher.ComputeHashAsync(stream));
    }

    [Fact]
    public async Task ComputeHash_NullStream_Throws()
    {
        Stream? missing = null;

        Assert.Throws<ArgumentNullException>(() => _hasher.ComputeHash(missing!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await _hasher.ComputeHashAsync(missing!));
    }

    [Fact]
    public async Task ComputeHashAsync_CanceledToken_Throws()
    {
        using var stream = new MemoryStream(RandomNumberGenerator.GetBytes(1024));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _hasher.ComputeHashAsync(stream, cts.Token));
    }
}
