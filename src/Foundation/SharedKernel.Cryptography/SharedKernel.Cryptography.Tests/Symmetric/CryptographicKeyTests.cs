using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class CryptographicKeyTests
{
    private static readonly byte[] Material = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    [Fact]
    public void Constructor_ValidArguments_ExposesIdAndMaterial()
    {
        var key = new CryptographicKey("key-1", Material);

        Assert.Equal("key-1", key.Id);
        Assert.Equal(Material, key.Material.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void Constructor_EmptyOrWhitespaceId_Throws(string id)
    {
        Assert.Throws<ArgumentException>(() => new CryptographicKey(id, Material));
    }

    [Fact]
    public void Constructor_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CryptographicKey(null!, Material));
    }

    [Fact]
    public void Constructor_IdOf255Utf8Bytes_IsAccepted()
    {
        string id = new string('é', 127) + "a";

        var key = new CryptographicKey(id, Material);

        Assert.Equal(id, key.Id);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(1000)]
    public void Constructor_IdLongerThan255Utf8Bytes_Throws(int asciiLength)
    {
        Assert.Throws<ArgumentException>(() => new CryptographicKey(new string('a', asciiLength), Material));
    }

    [Fact]
    public void Constructor_MultiByteIdOver255Utf8Bytes_Throws()
    {
        string id = new('é', 128);

        Assert.Throws<ArgumentException>(() => new CryptographicKey(id, Material));
    }

    [Fact]
    public void Constructor_EmptyMaterial_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CryptographicKey("key-1", ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Constructor_CopiesMaterial()
    {
        byte[] source = [.. Material];

        var key = new CryptographicKey("key-1", source);
        source[0] = 0xFF;

        Assert.Equal(0, key.Material[0]);
    }

    [Fact]
    public void ToString_ContainsIdButNotMaterial()
    {
        var key = new CryptographicKey("key-1", Material);

        string text = key.ToString();

        Assert.Equal("CryptographicKey { Id = key-1 }", text);
        Assert.DoesNotContain(Convert.ToBase64String(Material), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(Material), text, StringComparison.OrdinalIgnoreCase);
    }
}
