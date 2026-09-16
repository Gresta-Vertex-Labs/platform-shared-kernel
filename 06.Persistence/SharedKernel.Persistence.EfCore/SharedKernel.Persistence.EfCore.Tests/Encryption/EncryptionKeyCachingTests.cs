using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// <see cref="EncryptionOptionsKeyProvider"/> decode-once-per-config-value caching tests.
/// </summary>
public sealed class EncryptionKeyCachingTests
{
    /// <summary>A monitor whose current value a test can replace, as a configuration reload does.</summary>
    private sealed class ReloadableOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue { get; private set; } = value;

        public EncryptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;

        public void SimulateReload(EncryptionOptions newValue) => CurrentValue = newValue;
    }

    private static string Key(byte fill)
    {
        var bytes = new byte[32];
        Array.Fill(bytes, fill);
        return Convert.ToBase64String(bytes);
    }

    private static byte[] KeyBytes(byte fill) => Convert.FromBase64String(Key(fill));

    [Fact]
    public void GetCurrentKey_RepeatedCalls_ReturnsSameCachedKeyInstance()
    {
        var monitor = new ReloadableOptionsMonitor(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Key(0x11) },
        });
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        var key1 = provider.GetCurrentKey();
        var key2 = provider.GetCurrentKey();

        key1.Id.Should().Be("v1");
        key1.Should().BeSameAs(key2,
            "repeated calls for an unchanged configured value must reuse the decoded key, not re-decode Base64");
    }

    [Fact]
    public void GetKey_RepeatedCalls_ReturnsSameCachedKeyInstance()
    {
        var monitor = new ReloadableOptionsMonitor(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Key(0x11), ["v2"] = Key(0x22) },
        });
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        var key1 = provider.GetKey("v2");
        var key2 = provider.GetKey("v2");

        key1.Should().NotBeNull();
        key1.Should().BeSameAs(key2);
        key1!.Material.ToArray().Should().Equal(KeyBytes(0x22));
    }

    [Fact]
    public void Reload_ChangingKeyMaterialForSameVersion_IsReflected_NoStaleKey()
    {
        var monitor = new ReloadableOptionsMonitor(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Key(0x01) },
        });
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        var before = provider.GetCurrentKey();
        before.Material.ToArray().Should().Equal(KeyBytes(0x01));

        monitor.SimulateReload(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Key(0x02), ["v2"] = Key(0x03) },
        });

        provider.GetCurrentKey().Material.ToArray().Should().Equal(KeyBytes(0x02),
            "a stale cached key must not survive a change to the configured value");
        provider.GetKey("v2")!.Material.ToArray().Should().Equal(KeyBytes(0x03));
    }

    [Fact]
    public void InPlaceMutationOfKeyValue_IsReflected_WithoutAnyChangeNotification()
    {
        // The cache is validated against the configured Base64 string on every call, so it needs no
        // IOptionsMonitor.OnChange subscription to stay correct.
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Key(0x01) },
        };
        var provider = new EncryptionOptionsKeyProvider(new ReloadableOptionsMonitor(options), EncryptionVersionOverride.NoOp);

        _ = provider.GetCurrentKey();
        options.Keys["v1"] = Key(0x09);

        provider.GetCurrentKey().Material.ToArray().Should().Equal(KeyBytes(0x09));
    }

    [Fact]
    public void Reload_AddingVersionAndSwitchingCurrent_ReflectsNewKeyVersion()
    {
        var monitor = new ReloadableOptionsMonitor(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Key(0x11) },
        });
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        provider.GetCurrentKey().Id.Should().Be("v1");

        monitor.SimulateReload(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v2",
            Keys = { ["v1"] = Key(0x11), ["v2"] = Key(0x22) },
        });

        var after = provider.GetCurrentKey();
        after.Id.Should().Be("v2");
        after.Material.ToArray().Should().Equal(KeyBytes(0x22));
    }

    [Fact]
    public void Reload_RemovingVersion_GetKeyReturnsNull()
    {
        var monitor = new ReloadableOptionsMonitor(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v2",
            Keys = { ["v1"] = Key(0x11), ["v2"] = Key(0x22) },
        });
        var provider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);
        provider.GetKey("v1").Should().NotBeNull();

        monitor.SimulateReload(new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v2",
            Keys = { ["v2"] = Key(0x22) },
        });

        provider.GetKey("v1").Should().BeNull("a retired version must not be served from the cache");
    }
}
