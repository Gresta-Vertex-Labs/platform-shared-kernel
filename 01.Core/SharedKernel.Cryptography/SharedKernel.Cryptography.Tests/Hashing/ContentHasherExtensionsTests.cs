using System.Text;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Tests.Hashing;

public sealed class ContentHasherExtensionsTests
{
    private const string AbcDigestHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private readonly Sha256ContentHasher _hasher = new();

    [Fact]
    public void ComputeHashHex_ReturnsLowercaseHex()
    {
        string hex = _hasher.ComputeHashHex(Encoding.ASCII.GetBytes("abc"));

        Assert.Equal(AbcDigestHex, hex);
    }

    [Fact]
    public void ComputeHashBase64_ReturnsPaddedStandardBase64()
    {
        string base64 = _hasher.ComputeHashBase64(Encoding.ASCII.GetBytes("abc"));

        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", base64);
        Assert.Equal(Convert.FromHexString(AbcDigestHex), Convert.FromBase64String(base64));
    }

    [Fact]
    public void Extensions_NullHasher_Throw()
    {
        IContentHasher? missing = null;

        Assert.Throws<ArgumentNullException>(() => missing!.ComputeHashHex([1]));
        Assert.Throws<ArgumentNullException>(() => missing!.ComputeHashBase64([1]));
    }
}
