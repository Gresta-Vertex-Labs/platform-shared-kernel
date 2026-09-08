using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-139 (P-498/WO-081, AC#5 regression): confirms the config-backed default providers
/// (<see cref="EncryptionOptionsKeyProvider"/>/<see cref="NullEncryptionKeyProvider"/>) honestly
/// implement <see cref="ISynchronousEncryptionKeyProvider"/> (D-127), and that every pre-existing
/// synchronous <see cref="ISymmetricEncryptionService"/> round trip through
/// <see cref="AesGcmEncryptionService"/> still succeeds byte-for-byte identically — proving the
/// upstream <c>01.Core</c> P-492 capability gate does NOT regress any existing config-backed
/// <c>.WithEncryption()</c> consumer.
/// </summary>
public sealed class SynchronousMarkerRegressionTests
{
    private sealed class FixedOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    [Fact]
    public void EncryptionOptionsKeyProvider_Reports_IsGenuinelySynchronous_True()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(new byte[32]) }
        };
        var monitor = new FixedOptionsMonitor(options);
        var provider = new EncryptionOptionsKeyProvider(
            monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));

        EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(provider).Should().BeTrue(
            "EncryptionOptionsKeyProvider performs no I/O — every existing config-backed " +
            ".WithEncryption() consumer must keep working unchanged once 01.Core's P-492 gate ships");
    }

    [Fact]
    public void NullEncryptionKeyProvider_Reports_IsGenuinelySynchronous_True()
    {
        EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(NullEncryptionKeyProvider.Instance)
            .Should().BeTrue("NullEncryptionKeyProvider never performs I/O either — it throws synchronously");
    }

    [Fact]
    public void AesGcmEncryptionService_Wrapping_EncryptionOptionsKeyProvider_SyncEncrypt_DoesNotThrow()
    {
        // AC#5: the sync AesGcmEncryptionService.Encrypt/Decrypt members must remain usable end to
        // end when backed by the config-backed default — proving the P-492 gate never fires for
        // this provider.
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(new byte[32]) }
        };
        var monitor = new FixedOptionsMonitor(options);
        var keyProvider = new EncryptionOptionsKeyProvider(
            monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));
        var service = new AesGcmEncryptionService(keyProvider);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("regression-check");
        var associatedData = System.Text.Encoding.UTF8.GetBytes("public.t.c");

        var act = () => service.Encrypt(plaintext, associatedData);

        act.Should().NotThrow<NotSupportedException>(
            "the config-backed provider is honestly marked ISynchronousEncryptionKeyProvider, so " +
            "the sync bridge must never throw NotSupportedException");
    }

    [Fact]
    public void AesGcmEncryptionService_Wrapping_EncryptionOptionsKeyProvider_SyncRoundTrip_ByteForByteIdentical()
    {
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys = { ["v1"] = Convert.ToBase64String(new byte[32]) }
        };
        var monitor = new FixedOptionsMonitor(options);
        var keyProvider = new EncryptionOptionsKeyProvider(
            monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));
        var service = new AesGcmEncryptionService(keyProvider);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("regression-round-trip");
        var associatedData = System.Text.Encoding.UTF8.GetBytes("public.t.c");

        var payload = service.Encrypt(plaintext, associatedData);
        var result = service.Decrypt(payload, associatedData);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(plaintext);
    }
}
