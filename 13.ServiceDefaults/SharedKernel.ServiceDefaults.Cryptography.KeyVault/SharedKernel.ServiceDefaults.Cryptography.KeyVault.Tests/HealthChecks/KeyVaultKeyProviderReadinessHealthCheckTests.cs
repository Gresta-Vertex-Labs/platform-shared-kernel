using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Cryptography.KeyVault.Tests.HealthChecks;

public sealed class KeyVaultKeyProviderReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_IsHealthyTrue_ReportsHealthy()
    {
        var probe = Substitute.For<IEncryptionKeyProviderProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EncryptionKeyProviderHealth(IsHealthy: true, Description: null)));

        var healthCheck = new KeyVaultKeyProviderReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_IsHealthyFalse_ReportsUnhealthy_NeverDegraded()
    {
        var probe = Substitute.For<IEncryptionKeyProviderProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EncryptionKeyProviderHealth(IsHealthy: false, Description: "Vault unreachable.")));

        var healthCheck = new KeyVaultKeyProviderReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Status.Should().NotBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_IsHealthyFalse_SurfacesDescriptionInResult()
    {
        var probe = Substitute.For<IEncryptionKeyProviderProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EncryptionKeyProviderHealth(IsHealthy: false, Description: "DNS resolution failed for my-vault.vault.azure.net.")));

        var healthCheck = new KeyVaultKeyProviderReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Description.Should().Be("DNS resolution failed for my-vault.vault.azure.net.");
    }

    [Fact]
    public async Task CheckHealthAsync_IsHealthyFalse_NullDescription_DoesNotThrow()
    {
        // The probe contract allows a null Description even when unhealthy (defensive coverage —
        // the shipped AzureKeyVaultEncryptionKeyProvider always supplies exception.Message, but the
        // adapter must not assume every IEncryptionKeyProviderProbe implementation does).
        var probe = Substitute.For<IEncryptionKeyProviderProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EncryptionKeyProviderHealth(IsHealthy: false, Description: null)));

        var healthCheck = new KeyVaultKeyProviderReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().NotBeNullOrEmpty();
    }
}
