using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-144 (P-498/WO-081, D-132): <see cref="EncryptionStartupValidator"/> startup fail-fast tests —
/// proves the defensive <see cref="EncryptionKeyProviderCapabilities.IsGenuinelySynchronous"/> check
/// fires even though D-131 makes the happy-path misconfiguration unreachable through the builder
/// itself.
/// </summary>
public sealed class EncryptionStartupValidatorTests
{
    // A hand-rolled IEncryptionKeyProvider that deliberately does NOT implement
    // ISynchronousEncryptionKeyProvider — plausibly network-bound, exactly the shape this check
    // must reject.
    private sealed class UnmarkedFakeProvider : IEncryptionKeyProvider
    {
        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
            new(new CryptographicKey("v1", new byte[32]));

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
            new((CryptographicKey?)null);
    }

    [Fact]
    public void NoKeyProviderRegisteredUnderKeyedSlot_Fails()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var validator = new EncryptionStartupValidator(provider);

        var result = validator.Validate(null, new EncryptionStartupOptions());

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("no IEncryptionKeyProvider is registered");
    }

    [Fact]
    public void HandRolledProvider_WiredInDefianceOfTheBuilder_DoesNotImplementMarker_Fails()
    {
        // "Wired in defiance of the builder" — directly registering under this package's own
        // internal keyed-DI slot, bypassing .WithExternalEncryptionKeyProvider<TProvider>()
        // entirely, simulating a maintainer hand-constructing the pipeline.
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey, (_, _) => new UnmarkedFakeProvider());
        var provider = services.BuildServiceProvider();

        var validator = new EncryptionStartupValidator(provider);

        var result = validator.Validate(null, new EncryptionStartupOptions());

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(UnmarkedFakeProvider));
        result.FailureMessage.Should().Contain("ISynchronousEncryptionKeyProvider");
    }

    [Fact]
    public void ConfigBackedDefault_EncryptionOptionsKeyProvider_WiredCorrectly_Succeeds()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(new byte[32]) }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<EncryptionOptions>>(new FixedOptionsMonitor(options));
        services.AddSingleton<IEncryptionVersionOverride, EncryptionVersionOverride>();
        services.AddSingleton<EncryptionKeyByteCache>();
        services.AddScoped<EncryptionOptionsKeyProvider>();
        services.AddKeyedScoped<IEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey,
            (sp, _) => sp.GetRequiredService<EncryptionOptionsKeyProvider>());
        var provider = services.BuildServiceProvider();

        var validator = new EncryptionStartupValidator(provider);

        var result = validator.Validate(null, new EncryptionStartupOptions());

        result.Succeeded.Should().BeTrue(
            "the config-backed default is honestly marked ISynchronousEncryptionKeyProvider (D-127)");
    }

    [Fact]
    public void ExternalMode_PreWarmedEncryptionKeyProvider_WiredCorrectly_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionVersionOverride, EncryptionVersionOverride>();
        services.AddSingleton(sp =>
            new PreWarmedEncryptionKeyProvider(new FakeRemoteProvider(), sp.GetRequiredService<IEncryptionVersionOverride>()));
        services.AddKeyedSingleton<IEncryptionKeyProvider>(
            PersistenceEncryptionKeys.EncryptionKeyProviderKey,
            (sp, _) => sp.GetRequiredService<PreWarmedEncryptionKeyProvider>());
        var provider = services.BuildServiceProvider();

        var validator = new EncryptionStartupValidator(provider);

        var result = validator.Validate(null, new EncryptionStartupOptions());

        result.Succeeded.Should().BeTrue(
            "PreWarmedEncryptionKeyProvider honestly earns ISynchronousEncryptionKeyProvider by " +
            "construction (D-129), even though it wraps a genuinely network-bound inner provider");
    }

    // A stand-in for a genuinely network-bound provider — never implements
    // ISynchronousEncryptionKeyProvider, mirroring a real KMS-backed implementation.
    private sealed class FakeRemoteProvider : IEncryptionKeyProvider
    {
        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
            new(new CryptographicKey("v1", new byte[32]));

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
            new((CryptographicKey?)null);
    }

    private sealed class FixedOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }
}
