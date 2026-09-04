using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeEncryptionKeyProvider"/> against <c>IEncryptionKeyProvider</c>'s
/// documented contract. Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-56.
/// </summary>
public sealed class FakeEncryptionKeyProviderTests
{
    [Fact]
    public async Task Constructor_SeedsCurrentKey_Immediately()
    {
        var provider = new FakeEncryptionKeyProvider();

        var current = await provider.GetCurrentKeyAsync();

        Assert.Equal("v1", current.Id);
        Assert.Equal(32, current.Material.Length);
    }

    [Fact]
    public async Task Constructor_WithCustomCurrentKeyId_SeedsThatKeyInstead()
    {
        var provider = new FakeEncryptionKeyProvider(currentKeyId: "custom");

        Assert.Equal("custom", (await provider.GetCurrentKeyAsync()).Id);
    }

    [Fact]
    public async Task AddKey_RegistersAdditionalKeyVersion_ResolvableViaGetKeyAsync()
    {
        var provider = new FakeEncryptionKeyProvider();

        var added = provider.AddKey("v2");

        Assert.Equal(added, await provider.GetKeyAsync("v2"));
    }

    [Fact]
    public async Task AddKey_GeneratesFreshMaterial_DifferentAcrossKeyVersions()
    {
        var provider = new FakeEncryptionKeyProvider();
        var v1 = await provider.GetCurrentKeyAsync();

        var v2 = provider.AddKey("v2");

        Assert.NotEqual(v1.Material, v2.Material);
    }

    [Fact]
    public async Task SetCurrentKey_SwitchesWhichKeyIsCurrent()
    {
        var provider = new FakeEncryptionKeyProvider();
        var v2 = provider.AddKey("v2");

        provider.SetCurrentKey("v2");

        Assert.Equal(v2, await provider.GetCurrentKeyAsync());
    }

    [Fact]
    public async Task MultiKeyRotation_OlderKeyVersionRemainsResolvable_AfterCurrentKeySwitches()
    {
        var provider = new FakeEncryptionKeyProvider();
        var v1 = await provider.GetCurrentKeyAsync();
        provider.AddKey("v2");

        provider.SetCurrentKey("v2");

        Assert.Equal(v1, await provider.GetKeyAsync("v1"));
        Assert.Equal("v2", (await provider.GetCurrentKeyAsync()).Id);
    }

    [Fact]
    public async Task RemoveKey_MakesItUnresolvable()
    {
        var provider = new FakeEncryptionKeyProvider();
        provider.AddKey("v2");

        provider.RemoveKey("v1");

        Assert.Null(await provider.GetKeyAsync("v1"));
    }

    [Fact]
    public void RemoveKey_UnknownKeyId_IsANoOp()
    {
        var provider = new FakeEncryptionKeyProvider();

        var exception = Record.Exception(() => provider.RemoveKey("never-existed"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownKeyId_ReturnsNull() =>
        Assert.Null(await new FakeEncryptionKeyProvider().GetKeyAsync("does-not-exist"));
}
