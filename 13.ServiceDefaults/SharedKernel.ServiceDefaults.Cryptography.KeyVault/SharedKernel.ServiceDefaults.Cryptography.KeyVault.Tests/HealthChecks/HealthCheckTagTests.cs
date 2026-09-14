using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Cryptography.KeyVault.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddKeyVaultKeyProviderReadinessCheck_RegistersWithReadyEncryptionKeyProviderTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEncryptionKeyProviderProbe>());

        services.AddHealthChecks().AddKeyVaultKeyProviderReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.EncryptionKeyProvider);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.EncryptionKeyProvider, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
