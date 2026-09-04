using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Coverage for the <see cref="EncryptionKeyProviderHealth"/> record and, via a minimal
/// test-only <see cref="IEncryptionKeyProviderProbe"/> implementation, the shape of the
/// <see cref="IEncryptionKeyProviderProbe"/> contract itself. Real provider behavior (Azure Key
/// Vault) is covered in <c>SharedKernel.Cryptography.KeyVault.Azure.Tests</c> — this package ships
/// the contract only, no default implementation, mirroring
/// <see cref="IEncryptionKeyProvider"/>/<see cref="IEnvelopeEncryptionProvider"/>.
/// </summary>
public sealed class EncryptionKeyProviderHealthTests
{
    [Fact]
    public void Healthy_HasNullDescription()
    {
        var health = new EncryptionKeyProviderHealth(IsHealthy: true, Description: null);

        Assert.True(health.IsHealthy);
        Assert.Null(health.Description);
    }

    [Fact]
    public void Unhealthy_CarriesDescription()
    {
        var health = new EncryptionKeyProviderHealth(IsHealthy: false, Description: "vault unreachable");

        Assert.False(health.IsHealthy);
        Assert.Equal("vault unreachable", health.Description);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        var first = new EncryptionKeyProviderHealth(IsHealthy: false, Description: "boom");
        var second = new EncryptionKeyProviderHealth(IsHealthy: false, Description: "boom");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ProbeAsync_ContractShape_ReturnsHealthWithoutThrowing()
    {
        IEncryptionKeyProviderProbe probe = new AlwaysHealthyProbe();

        EncryptionKeyProviderHealth result = await probe.ProbeAsync();

        Assert.True(result.IsHealthy);
    }

    /// <summary>
    /// A minimal, in-process implementation proving the contract's shape is usable — never
    /// registered by <c>AddSharedKernelCryptography</c>, which ships no default implementation of
    /// this opt-in interface.
    /// </summary>
    private sealed class AlwaysHealthyProbe : IEncryptionKeyProviderProbe
    {
        public Task<EncryptionKeyProviderHealth> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new EncryptionKeyProviderHealth(IsHealthy: true, Description: null));
    }
}
