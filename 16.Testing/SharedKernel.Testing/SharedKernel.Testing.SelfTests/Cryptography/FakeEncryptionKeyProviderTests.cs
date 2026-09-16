using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeEncryptionKeyProvider"/> against both <c>IEncryptionKeyProvider</c> and
/// <c>ISynchronousEncryptionKeyProvider</c>: random 32-byte keys that can be added, rotated and removed, with the
/// synchronous and asynchronous members always agreeing.
/// </summary>
public sealed class FakeEncryptionKeyProviderTests
{
    [Fact]
    public async Task Constructor_SeedsARandom32ByteCurrentKey()
    {
        var provider = new FakeEncryptionKeyProvider();

        CryptographicKey current = await provider.GetCurrentKeyAsync();

        Assert.Equal("v1", current.Id);
        Assert.Equal("v1", provider.CurrentKeyId);
        Assert.Equal(32, current.Material.Length);
    }

    [Fact]
    public void Constructor_WithCustomCurrentKeyId_SeedsThatKeyInstead()
    {
        var provider = new FakeEncryptionKeyProvider(currentKeyId: "custom");

        Assert.Equal("custom", provider.GetCurrentKey().Id);
        Assert.Equal("custom", provider.CurrentKeyId);
    }

    [Fact]
    public async Task SynchronousAndAsynchronousMembers_ReturnTheSameKeys()
    {
        var provider = new FakeEncryptionKeyProvider();
        provider.AddKey("v2");

        Assert.Same(provider.GetCurrentKey(), await provider.GetCurrentKeyAsync());
        Assert.Same(provider.GetKey("v2"), await provider.GetKeyAsync("v2"));
        Assert.Null(provider.GetKey("missing"));
        Assert.Null(await provider.GetKeyAsync("missing"));
    }

    [Fact]
    public async Task AddKey_RegistersAnAdditionalKey_ResolvableById()
    {
        var provider = new FakeEncryptionKeyProvider();

        CryptographicKey added = provider.AddKey("v2");

        Assert.Same(added, await provider.GetKeyAsync("v2"));
        Assert.Equal("v1", provider.CurrentKeyId);
    }

    [Fact]
    public void AddKey_GeneratesFreshMaterial_DifferentAcrossKeys()
    {
        var provider = new FakeEncryptionKeyProvider();
        CryptographicKey v1 = provider.GetCurrentKey();

        CryptographicKey v2 = provider.AddKey("v2");

        Assert.NotEqual(v1.Material.ToArray(), v2.Material.ToArray());
    }

    [Fact]
    public void AddKey_ExistingId_ReplacesTheKey()
    {
        var provider = new FakeEncryptionKeyProvider();
        CryptographicKey original = provider.GetCurrentKey();

        CryptographicKey replacement = provider.AddKey("v1");

        Assert.NotSame(original, replacement);
        Assert.Same(replacement, provider.GetCurrentKey());
    }

    [Fact]
    public async Task SetCurrentKey_SwitchesTheCurrentKey_AndKeepsOlderKeysResolvable()
    {
        var provider = new FakeEncryptionKeyProvider();
        CryptographicKey v1 = provider.GetCurrentKey();
        CryptographicKey v2 = provider.AddKey("v2");

        provider.SetCurrentKey("v2");

        Assert.Same(v2, await provider.GetCurrentKeyAsync());
        Assert.Same(v2, provider.GetCurrentKey());
        Assert.Equal("v2", provider.CurrentKeyId);
        Assert.Same(v1, await provider.GetKeyAsync("v1"));
    }

    [Fact]
    public void SetCurrentKey_UnknownKeyId_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => new FakeEncryptionKeyProvider().SetCurrentKey("never-added"));

    [Fact]
    public async Task RemoveKey_MakesTheKeyUnresolvable()
    {
        var provider = new FakeEncryptionKeyProvider();
        provider.AddKey("v2");
        provider.SetCurrentKey("v2");

        provider.RemoveKey("v1");

        Assert.Null(await provider.GetKeyAsync("v1"));
        Assert.Null(provider.GetKey("v1"));
    }

    [Fact]
    public void RemoveKey_UnknownKeyId_IsANoOp()
    {
        var provider = new FakeEncryptionKeyProvider();

        var exception = Record.Exception(() => provider.RemoveKey("never-existed"));

        Assert.Null(exception);
    }

    [Fact]
    public void GetAsync_CompletesSynchronously()
    {
        var provider = new FakeEncryptionKeyProvider();

        Assert.True(provider.GetCurrentKeyAsync().IsCompletedSuccessfully);
        Assert.True(provider.GetKeyAsync("v1").IsCompletedSuccessfully);
    }
}
