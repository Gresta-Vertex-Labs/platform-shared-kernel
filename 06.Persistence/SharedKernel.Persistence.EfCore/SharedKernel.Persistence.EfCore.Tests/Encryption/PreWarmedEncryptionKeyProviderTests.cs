using FluentAssertions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-141 (P-498/WO-081): <see cref="PreWarmedEncryptionKeyProvider"/> unit tests — proves the
/// "never touches Inner from the sync-contract members" guarantee structurally via a call-counting
/// inner provider, not by inspection.
/// </summary>
public sealed class PreWarmedEncryptionKeyProviderTests
{
    // A call-counting IEncryptionKeyProvider test double, distinct from 16.Testing's fakes, so this
    // test can assert EXACTLY which member was invoked and how many times.
    private sealed class CallCountingInnerProvider : IEncryptionKeyProvider
    {
        private readonly Dictionary<string, byte[]> _keys = new();

        public int GetCurrentKeyAsyncCallCount { get; private set; }
        public int GetKeyAsyncCallCount { get; private set; }

        public string? CurrentKeyId { get; set; }

        public void AddKey(string keyId, byte fill)
        {
            var material = new byte[32];
            Array.Fill(material, fill);
            _keys[keyId] = material;
        }

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
        {
            GetCurrentKeyAsyncCallCount++;
            return new ValueTask<CryptographicKey>(new CryptographicKey(CurrentKeyId!, _keys[CurrentKeyId!]));
        }

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
        {
            GetKeyAsyncCallCount++;
            return new ValueTask<CryptographicKey?>(
                _keys.TryGetValue(keyId, out var material) ? new CryptographicKey(keyId, material) : null);
        }
    }

    [Fact]
    public async Task WarmedVersion_RoundTrips_ViaGetKeyAsync()
    {
        var inner = new CallCountingInnerProvider();
        inner.AddKey("v1", 0x11);
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmVersionAsync("v1");
        var key = await provider.GetKeyAsync("v1");

        key.Should().NotBeNull();
        key!.Id.Should().Be("v1");
        key.Material.Should().Equal(Enumerable.Repeat((byte)0x11, 32));
    }

    [Fact]
    public async Task UnwarmedVersion_GetKeyAsync_Returns_Null_Never_Throws()
    {
        var inner = new CallCountingInnerProvider();
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        var key = await provider.GetKeyAsync("never-warmed");

        key.Should().BeNull("a cache miss fails closed, non-blocking — mirroring the existing " +
            "unknown/removed-key case, never throwing and never touching Inner");
        inner.GetKeyAsyncCallCount.Should().Be(0, "GetKeyAsync must never touch Inner");
        inner.GetCurrentKeyAsyncCallCount.Should().Be(0, "GetKeyAsync must never touch Inner");
    }

    [Fact]
    public void GetCurrentKeyAsync_BeforeAnyWarmCall_Throws_InvalidOperationException_Synchronously()
    {
        var inner = new CallCountingInnerProvider();
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        // Proven via a call that never awaits — if this threw only when awaited, the delegate
        // invocation itself (before any `await`) would not throw here.
        var act = () => provider.GetCurrentKeyAsync();

        act.Should().Throw<InvalidOperationException>();
        inner.GetCurrentKeyAsyncCallCount.Should().Be(0, "GetCurrentKeyAsync must never touch Inner");
    }

    [Fact]
    public async Task GetCurrentKeyAsync_AfterWarmCurrentAsync_Succeeds()
    {
        var inner = new CallCountingInnerProvider { CurrentKeyId = "v1" };
        inner.AddKey("v1", 0x22);
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmCurrentAsync();
        var key = await provider.GetCurrentKeyAsync();

        key.Id.Should().Be("v1");
        key.Material.Should().Equal(Enumerable.Repeat((byte)0x22, 32));
    }

    [Fact]
    public async Task ZeroCalls_Ever_Originate_From_GetCurrentKeyAsync_Or_GetKeyAsync_Themselves()
    {
        // Structural proof (T-141): every call to Inner originates ONLY from
        // WarmCurrentAsync/WarmVersionAsync — never from the two IEncryptionKeyProvider members
        // themselves, even on the successful/warm path.
        var inner = new CallCountingInnerProvider { CurrentKeyId = "v1" };
        inner.AddKey("v1", 0x33);
        inner.AddKey("v2", 0x44);
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmCurrentAsync();
        await provider.WarmVersionAsync("v2");

        inner.GetCurrentKeyAsyncCallCount.Should().Be(1, "exactly one warm call for the current version");
        inner.GetKeyAsyncCallCount.Should().Be(1, "exactly one warm call for the explicit version");

        // Now exercise GetCurrentKeyAsync/GetKeyAsync repeatedly — call counts on Inner must not move.
        _ = await provider.GetCurrentKeyAsync();
        _ = await provider.GetCurrentKeyAsync();
        _ = await provider.GetKeyAsync("v1");
        _ = await provider.GetKeyAsync("v2");
        _ = await provider.GetKeyAsync("unknown");

        inner.GetCurrentKeyAsyncCallCount.Should().Be(1,
            "GetCurrentKeyAsync (the public IEncryptionKeyProvider member) must never touch Inner");
        inner.GetKeyAsyncCallCount.Should().Be(1,
            "GetKeyAsync (the public IEncryptionKeyProvider member) must never touch Inner");
    }

    [Fact]
    public async Task WarmCurrentAsync_AlreadyWarm_IsNoOp_NeverTouchesInner()
    {
        var inner = new CallCountingInnerProvider { CurrentKeyId = "v1" };
        inner.AddKey("v1", 0x55);
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmCurrentAsync();
        await provider.WarmCurrentAsync();
        await provider.WarmCurrentAsync();

        inner.GetCurrentKeyAsyncCallCount.Should().Be(1,
            "warming an already-warm current version must be a no-op — never re-touches Inner");
    }

    [Fact]
    public async Task WarmVersionAsync_AlreadyWarm_IsNoOp_NeverTouchesInner()
    {
        var inner = new CallCountingInnerProvider();
        inner.AddKey("v1", 0x66);
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmVersionAsync("v1");
        await provider.WarmVersionAsync("v1");

        inner.GetKeyAsyncCallCount.Should().Be(1,
            "warming an already-warm version must be a no-op — never re-touches Inner");
    }

    [Fact]
    public async Task WarmVersionAsync_UnknownVersion_DoesNotCacheAnything()
    {
        var inner = new CallCountingInnerProvider(); // no keys added at all
        var provider = new PreWarmedEncryptionKeyProvider(inner, EncryptionVersionOverride.NoOp);

        await provider.WarmVersionAsync("does-not-exist");
        var key = await provider.GetKeyAsync("does-not-exist");

        key.Should().BeNull();
    }

    [Fact]
    public async Task OverrideVersion_TakesPrecedence_Over_CurrentVersionTag()
    {
        var inner = new CallCountingInnerProvider { CurrentKeyId = "v1" };
        inner.AddKey("v1", 0x11);
        inner.AddKey("v2", 0x22);

        var versionOverride = new EncryptionVersionOverride();
        var provider = new PreWarmedEncryptionKeyProvider(inner, versionOverride);

        await provider.WarmCurrentAsync(); // warms v1 as the current tag

        versionOverride.OverrideVersion = "v2";
        await provider.WarmCurrentAsync(); // must warm v2 (the override), via GetKeyAsync-shaped resolution

        var key = await provider.GetCurrentKeyAsync();

        key.Id.Should().Be("v2", "OverrideVersion ?? _currentVersionTag must resolve to the override when set");
    }
}
