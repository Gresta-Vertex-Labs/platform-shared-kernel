using FluentAssertions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// <see cref="PreWarmedEncryptionKeyProvider"/> unit tests — proves the synchronous members never touch the
/// wrapped asynchronous provider, structurally via <see cref="FakeRemoteEncryptionKeyProvider"/>'s call counters.
/// </summary>
public sealed class PreWarmedEncryptionKeyProviderTests
{
    [Fact]
    public void Implements_TheSynchronousProviderContract_OverAnAsyncOnlyInner()
    {
        var inner = new FakeRemoteEncryptionKeyProvider();

        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        inner.Should().NotBeAssignableTo<ISynchronousEncryptionKeyProvider>(
            "the fake stands in for a KMS that only completes asynchronously");
        provider.Should().BeAssignableTo<ISynchronousEncryptionKeyProvider>();
    }

    [Fact]
    public async Task WarmedVersion_IsServed_ByGetKey()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var expected = inner.AddKey("v2");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmVersionAsync("v2");
        var key = provider.GetKey("v2");

        key.Should().NotBeNull();
        key!.Id.Should().Be("v2");
        key.Material.ToArray().Should().Equal(expected.Material.ToArray());
    }

    [Fact]
    public void UnwarmedVersion_GetKey_Returns_Null_Never_Throws_NeverTouchesInner()
    {
        var inner = new FakeRemoteEncryptionKeyProvider();
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        var key = provider.GetKey("never-warmed");

        key.Should().BeNull("a cache miss fails closed, exactly like an unknown/removed key");
        inner.KeyCallCount.Should().Be(0);
        inner.CurrentKeyCallCount.Should().Be(0);
    }

    [Fact]
    public void GetCurrentKey_BeforeAnyWarmCall_Throws_InvalidOperationException_NeverTouchesInner()
    {
        var inner = new FakeRemoteEncryptionKeyProvider();
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        var act = () => provider.GetCurrentKey();

        act.Should().Throw<InvalidOperationException>().WithMessage("*not been warmed*");
        inner.CurrentKeyCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentKey_AfterWarmCurrentAsync_ReturnsInnerCurrentKey()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmCurrentAsync();
        var key = provider.GetCurrentKey();

        var expected = await inner.GetCurrentKeyAsync();
        key.Id.Should().Be("v1");
        key.Material.ToArray().Should().Equal(expected.Material.ToArray());
    }

    [Fact]
    public async Task ZeroCalls_Ever_Originate_From_GetCurrentKey_Or_GetKey()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        inner.AddKey("v2");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmCurrentAsync();
        await provider.WarmVersionAsync("v2");

        inner.CurrentKeyCallCount.Should().Be(1, "exactly one warm call for the current version");
        inner.KeyCallCount.Should().Be(1, "exactly one warm call for the explicit version");

        _ = provider.GetCurrentKey();
        _ = provider.GetCurrentKey();
        _ = provider.GetKey("v1");
        _ = provider.GetKey("v2");
        _ = provider.GetKey("unknown");

        inner.CurrentKeyCallCount.Should().Be(1, "GetCurrentKey must never touch Inner");
        inner.KeyCallCount.Should().Be(1, "GetKey must never touch Inner");
    }

    [Fact]
    public async Task WarmCurrentAsync_AlreadyWarm_IsNoOp_NeverTouchesInner()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmCurrentAsync();
        await provider.WarmCurrentAsync();
        await provider.WarmCurrentAsync();

        inner.CurrentKeyCallCount.Should().Be(1);
    }

    [Fact]
    public async Task WarmVersionAsync_AlreadyWarm_IsNoOp_NeverTouchesInner()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmVersionAsync("v1");
        await provider.WarmVersionAsync("v1");

        inner.KeyCallCount.Should().Be(1);
    }

    [Fact]
    public async Task WarmVersionAsync_UnknownVersion_DoesNotCacheAnything()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmVersionAsync("does-not-exist");

        provider.GetKey("does-not-exist").Should().BeNull();
    }

    [Fact]
    public async Task WarmCurrentAsync_HonoursCancellation()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await provider.WarmCurrentAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.Invoking(p => p.GetCurrentKey()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task OverrideVersion_TakesPrecedence_Over_CurrentVersionTag()
    {
        var inner = new FakeRemoteEncryptionKeyProvider("v1");
        inner.AddKey("v2");

        var versionOverride = new EncryptionVersionOverride();
        var provider = new PreWarmedEncryptionKeyProvider(inner, versionOverride);

        await provider.WarmCurrentAsync(); // warms v1 as the current tag

        versionOverride.OverrideVersion = "v2";
        await provider.WarmCurrentAsync(); // must warm v2 (the override)

        provider.GetCurrentKey().Id.Should().Be("v2");

        versionOverride.OverrideVersion = null;
        provider.GetCurrentKey().Id.Should().Be("v1");
    }
}
