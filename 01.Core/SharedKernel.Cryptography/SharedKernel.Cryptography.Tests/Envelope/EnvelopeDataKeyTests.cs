using SharedKernel.Cryptography.Envelope;

namespace SharedKernel.Cryptography.Tests.Envelope;

public sealed class EnvelopeDataKeyTests
{
    private static readonly byte[] Plaintext = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
    private static readonly byte[] Wrapped = [9, 8, 7, 6];

    [Fact]
    public void Constructor_ValidArguments_ExposesThem()
    {
        using var key = new EnvelopeDataKey(Plaintext, Wrapped, "master-1");

        Assert.Equal(Plaintext, key.PlaintextKey.ToArray());
        Assert.Equal(Wrapped, key.WrappedKey.ToArray());
        Assert.Equal("master-1", key.MasterKeyId);
    }

    [Fact]
    public void Constructor_CopiesBuffers()
    {
        byte[] plaintext = [.. Plaintext];
        byte[] wrapped = [.. Wrapped];

        using var key = new EnvelopeDataKey(plaintext, wrapped, "master-1");
        plaintext[0] = 0xFF;
        wrapped[0] = 0xFF;

        Assert.Equal(Plaintext, key.PlaintextKey.ToArray());
        Assert.Equal(Wrapped, key.WrappedKey.ToArray());
    }

    [Fact]
    public void Constructor_EmptyBuffers_Throw()
    {
        Assert.Throws<ArgumentException>(() => new EnvelopeDataKey(ReadOnlySpan<byte>.Empty, Wrapped, "master-1"));
        Assert.Throws<ArgumentException>(() => new EnvelopeDataKey(Plaintext, ReadOnlySpan<byte>.Empty, "master-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_InvalidMasterKeyId_Throws(string masterKeyId)
    {
        Assert.Throws<ArgumentException>(() => new EnvelopeDataKey(Plaintext, Wrapped, masterKeyId));
    }

    [Fact]
    public void Constructor_NullMasterKeyId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EnvelopeDataKey(Plaintext, Wrapped, null!));
    }

    [Fact]
    public void Dispose_ZeroesPlaintextKey()
    {
        var key = new EnvelopeDataKey(Plaintext, Wrapped, "master-1");
        ReadOnlySpan<byte> view = key.PlaintextKey;

        key.Dispose();

        Assert.All(view.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void PlaintextKey_AfterDispose_Throws()
    {
        var key = new EnvelopeDataKey(Plaintext, Wrapped, "master-1");

        key.Dispose();

        Assert.Throws<ObjectDisposedException>(() => key.PlaintextKey.Length);
    }

    [Fact]
    public void Dispose_KeepsWrappedKeyAndMasterKeyIdAndIsIdempotent()
    {
        var key = new EnvelopeDataKey(Plaintext, Wrapped, "master-1");

        key.Dispose();
        key.Dispose();

        Assert.Equal(Wrapped, key.WrappedKey.ToArray());
        Assert.Equal("master-1", key.MasterKeyId);
    }
}
