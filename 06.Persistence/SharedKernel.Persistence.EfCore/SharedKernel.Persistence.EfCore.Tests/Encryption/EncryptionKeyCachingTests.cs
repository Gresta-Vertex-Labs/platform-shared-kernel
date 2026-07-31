using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// WO-051/P-323 (T-92): <see cref="EncryptionOptionsKeyProvider"/>/<see cref="EncryptionKeyByteCache"/>
/// decode-once-per-config-value caching tests.
/// </summary>
public sealed class EncryptionKeyCachingTests
{
    /// <summary>
    /// A monitor whose <see cref="OnChange"/> captures the listener registered by
    /// <see cref="EncryptionKeyByteCache"/>'s constructor, so tests can simulate a config/rotation
    /// reload by invoking <see cref="SimulateReload"/> directly — unlike the fixed/no-op monitors
    /// used elsewhere in this test project, which never actually invoke the listener.
    /// </summary>
    private sealed class ReloadCapableOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        private Action<EncryptionOptions, string?>? _listener;

        public EncryptionOptions CurrentValue { get; private set; } = value;

        public EncryptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener)
        {
            _listener = listener;
            return null;
        }

        public void SimulateReload(EncryptionOptions newValue)
        {
            CurrentValue = newValue;
            _listener?.Invoke(newValue, null);
        }
    }

    private static byte[] MakeKeyBytes(byte fill)
    {
        var bytes = new byte[32];
        Array.Fill(bytes, fill);
        return bytes;
    }

    // -------------------------------------------------------------------------
    // EncryptionKeyByteCache — decode-once-per-version proof via reference equality. Convert.
    // FromBase64String always allocates a NEW array on every call, so the cache returning the SAME
    // array instance across repeated calls is direct proof the Base64 value was decoded exactly
    // once (a re-decode would necessarily produce a different, non-reference-equal array).
    // -------------------------------------------------------------------------

    [Fact]
    public void GetOrDecode_RepeatedCallsForSameVersion_ReturnsSameArrayInstance_DecodedOnce()
    {
        var monitor = new ReloadCapableOptionsMonitor(new EncryptionOptions());
        var cache = new EncryptionKeyByteCache(monitor);
        var base64 = Convert.ToBase64String(MakeKeyBytes(0x01));

        var first = cache.GetOrDecode("v1", base64);
        var second = cache.GetOrDecode("v1", base64);
        var third = cache.GetOrDecode("v1", base64);

        ReferenceEquals(first, second).Should().BeTrue(
            "the cache must decode the Base64 value only once per version and reuse the same byte[] instance");
        ReferenceEquals(second, third).Should().BeTrue();
        first.Should().Equal(MakeKeyBytes(0x01));
    }

    [Fact]
    public void GetOrDecode_DifferentVersions_DecodedIndependently_ReturnsDistinctArrays()
    {
        var monitor = new ReloadCapableOptionsMonitor(new EncryptionOptions());
        var cache = new EncryptionKeyByteCache(monitor);

        var v1 = cache.GetOrDecode("v1", Convert.ToBase64String(MakeKeyBytes(0x01)));
        var v2 = cache.GetOrDecode("v2", Convert.ToBase64String(MakeKeyBytes(0x02)));

        v1.Should().Equal(MakeKeyBytes(0x01));
        v2.Should().Equal(MakeKeyBytes(0x02));
        ReferenceEquals(v1, v2).Should().BeFalse();
    }

    [Fact]
    public void GetOrDecode_AfterSimulatedOptionsReload_CacheCleared_NextCallReflectsUpdatedValue()
    {
        // Arrange — initial key material for v1.
        var initialOptions = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(MakeKeyBytes(0x01)) },
        };
        var monitor = new ReloadCapableOptionsMonitor(initialOptions);
        var cache = new EncryptionKeyByteCache(monitor);

        var before = cache.GetOrDecode("v1", initialOptions.Keys["v1"]);
        before.Should().Equal(MakeKeyBytes(0x01));

        // Act — simulate a rotation-driven reload that changes v1's key material (and adds v2).
        var reloadedOptions = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys =
            {
                ["v1"] = Convert.ToBase64String(MakeKeyBytes(0x02)),
                ["v2"] = Convert.ToBase64String(MakeKeyBytes(0x03)),
            },
        };
        monitor.SimulateReload(reloadedOptions);

        var afterV1 = cache.GetOrDecode("v1", reloadedOptions.Keys["v1"]);
        var afterV2 = cache.GetOrDecode("v2", reloadedOptions.Keys["v2"]);

        // Assert — the cache was cleared on reload, so the NEW v1 bytes are decoded (not the stale
        // cached ones), and the newly-added v2 version resolves correctly.
        afterV1.Should().Equal(MakeKeyBytes(0x02));
        afterV1.Should().NotEqual(before, "a stale cached value must not survive a reload");
        afterV2.Should().Equal(MakeKeyBytes(0x03));
    }

    // -------------------------------------------------------------------------
    // EncryptionOptionsKeyProvider.GetCurrentKey()/GetKey() — repeated calls reuse the cached bytes.
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCurrentKey_RepeatedCalls_ReturnsSameCachedKeyBytesInstance()
    {
        var opts = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(MakeKeyBytes(0x11)) },
        };
        var monitor = new ReloadCapableOptionsMonitor(opts);
        var byteCache = new EncryptionKeyByteCache(monitor);
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp, byteCache);

        var key1 = provider.GetCurrentKey();
        var key2 = provider.GetCurrentKey();

        key1.Id.Should().Be("v1");
        ReferenceEquals(key1.Material, key2.Material).Should().BeTrue(
            "repeated GetCurrentKey calls for the same version must reuse the cached decoded key bytes, " +
            "not re-decode Base64 on every call");
    }

    [Fact]
    public void GetKey_RepeatedCalls_ReturnsSameCachedKeyBytesInstance()
    {
        var opts = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys =
            {
                ["v1"] = Convert.ToBase64String(MakeKeyBytes(0x11)),
                ["v2"] = Convert.ToBase64String(MakeKeyBytes(0x22)),
            },
        };
        var monitor = new ReloadCapableOptionsMonitor(opts);
        var byteCache = new EncryptionKeyByteCache(monitor);
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp, byteCache);

        var key1 = provider.GetKey("v2");
        var key2 = provider.GetKey("v2");

        key1.Should().NotBeNull();
        key2.Should().NotBeNull();
        ReferenceEquals(key1!.Material, key2!.Material).Should().BeTrue(
            "repeated GetKey calls for the same version must reuse the cached decoded key bytes");
    }

    [Fact]
    public void GetCurrentKey_AfterSimulatedRotationReload_ReflectsNewKeyVersion()
    {
        // A rotation adding a new key version and switching CurrentVersion must be reflected on the
        // next GetCurrentKey call — proving the cache clear does not stale-lock key resolution.
        var opts = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(MakeKeyBytes(0x11)) },
        };
        var monitor = new ReloadCapableOptionsMonitor(opts);
        var byteCache = new EncryptionKeyByteCache(monitor);
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp, byteCache);

        var before = provider.GetCurrentKey();
        before.Id.Should().Be("v1");

        var reloaded = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v2",
            Keys =
            {
                ["v1"] = Convert.ToBase64String(MakeKeyBytes(0x11)),
                ["v2"] = Convert.ToBase64String(MakeKeyBytes(0x22)),
            },
        };
        monitor.SimulateReload(reloaded);

        var after = provider.GetCurrentKey();
        after.Id.Should().Be("v2");
        after.Material.Should().Equal(MakeKeyBytes(0x22));
    }
}
