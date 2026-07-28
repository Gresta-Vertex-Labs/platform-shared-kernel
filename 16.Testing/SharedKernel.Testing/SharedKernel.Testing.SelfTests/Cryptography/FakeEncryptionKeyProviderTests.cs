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
    public void Constructor_SeedsCurrentKey_Immediately()
    {
        var provider = new FakeEncryptionKeyProvider();

        var current = provider.GetCurrentKey();

        Assert.Equal("v1", current.Id);
        Assert.Equal(32, current.Material.Length);
    }

    [Fact]
    public void Constructor_WithCustomCurrentKeyId_SeedsThatKeyInstead()
    {
        var provider = new FakeEncryptionKeyProvider(currentKeyId: "custom");

        Assert.Equal("custom", provider.GetCurrentKey().Id);
    }

    [Fact]
    public void AddKey_RegistersAdditionalKeyVersion_ResolvableViaGetKey()
    {
        var provider = new FakeEncryptionKeyProvider();

        var added = provider.AddKey("v2");

        Assert.Equal(added, provider.GetKey("v2"));
    }

    [Fact]
    public void AddKey_GeneratesFreshMaterial_DifferentAcrossKeyVersions()
    {
        var provider = new FakeEncryptionKeyProvider();
        var v1 = provider.GetCurrentKey();

        var v2 = provider.AddKey("v2");

        Assert.NotEqual(v1.Material, v2.Material);
    }

    [Fact]
    public void SetCurrentKey_SwitchesWhichKeyIsCurrent()
    {
        var provider = new FakeEncryptionKeyProvider();
        var v2 = provider.AddKey("v2");

        provider.SetCurrentKey("v2");

        Assert.Equal(v2, provider.GetCurrentKey());
    }

    [Fact]
    public void MultiKeyRotation_OlderKeyVersionRemainsResolvable_AfterCurrentKeySwitches()
    {
        var provider = new FakeEncryptionKeyProvider();
        var v1 = provider.GetCurrentKey();
        provider.AddKey("v2");

        provider.SetCurrentKey("v2");

        Assert.Equal(v1, provider.GetKey("v1"));
        Assert.Equal("v2", provider.GetCurrentKey().Id);
    }

    [Fact]
    public void RemoveKey_MakesItUnresolvable()
    {
        var provider = new FakeEncryptionKeyProvider();
        provider.AddKey("v2");

        provider.RemoveKey("v1");

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
    public void GetKey_UnknownKeyId_ReturnsNull() =>
        Assert.Null(new FakeEncryptionKeyProvider().GetKey("does-not-exist"));
}
