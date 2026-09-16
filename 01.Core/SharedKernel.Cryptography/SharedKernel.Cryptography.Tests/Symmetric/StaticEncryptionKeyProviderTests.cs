using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.TestDoubles;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class StaticEncryptionKeyProviderTests
{
    private readonly CryptographicKey _old = TestKeys.Create("2026-03");
    private readonly CryptographicKey _current = TestKeys.Create("2026-09");

    [Fact]
    public void GetCurrentKey_ReturnsKeyWithCurrentId()
    {
        var provider = new StaticEncryptionKeyProvider("2026-09", [_old, _current]);

        Assert.Same(_current, provider.GetCurrentKey());
    }

    [Fact]
    public void GetKey_KnownIds_ReturnKeys()
    {
        var provider = new StaticEncryptionKeyProvider("2026-09", [_old, _current]);

        Assert.Same(_old, provider.GetKey("2026-03"));
        Assert.Same(_current, provider.GetKey("2026-09"));
    }

    [Theory]
    [InlineData("2026-01")]
    [InlineData("")]
    [InlineData("2026-09 ")]
    public void GetKey_UnknownId_ReturnsNull(string keyId)
    {
        var provider = new StaticEncryptionKeyProvider("2026-09", [_old, _current]);

        Assert.Null(provider.GetKey(keyId));
    }

    [Fact]
    public void GetKey_IsCaseSensitive()
    {
        var provider = new StaticEncryptionKeyProvider("A", [TestKeys.Create("A")]);

        Assert.Null(provider.GetKey("a"));
    }

    [Fact]
    public async Task GetKey_NullId_Throws()
    {
        var provider = new StaticEncryptionKeyProvider("2026-09", [_current]);

        Assert.Throws<ArgumentNullException>(() => provider.GetKey(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await provider.GetKeyAsync(null!));
    }

    [Fact]
    public async Task AsyncMembers_MatchSynchronousMembers()
    {
        var provider = new StaticEncryptionKeyProvider("2026-09", [_old, _current]);

        Assert.Same(provider.GetCurrentKey(), await provider.GetCurrentKeyAsync());
        Assert.Same(provider.GetKey("2026-03"), await provider.GetKeyAsync("2026-03"));
        Assert.Null(await provider.GetKeyAsync("missing"));
    }

    [Fact]
    public void Constructor_DuplicateIds_Throws()
    {
        Assert.Throws<ArgumentException>(() => new StaticEncryptionKeyProvider("k", [TestKeys.Create("k"), TestKeys.Create("k")]));
    }

    [Fact]
    public void Constructor_CurrentIdMissing_Throws()
    {
        Assert.Throws<ArgumentException>(() => new StaticEncryptionKeyProvider("2026-12", [_old, _current]));
    }

    [Fact]
    public void Constructor_NoKeys_Throws()
    {
        Assert.Throws<ArgumentException>(() => new StaticEncryptionKeyProvider("k", []));
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new StaticEncryptionKeyProvider(" ", [_current]));
        Assert.Throws<ArgumentNullException>(() => new StaticEncryptionKeyProvider(null!, [_current]));
        Assert.Throws<ArgumentNullException>(() => new StaticEncryptionKeyProvider("2026-09", null!));
        Assert.Throws<ArgumentNullException>(() => new StaticEncryptionKeyProvider("2026-09", [_current, null!]));
    }
}
