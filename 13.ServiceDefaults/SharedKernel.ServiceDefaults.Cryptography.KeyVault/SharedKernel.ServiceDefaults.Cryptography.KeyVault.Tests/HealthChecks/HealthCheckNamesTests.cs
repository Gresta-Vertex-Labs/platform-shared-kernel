using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Cryptography.KeyVault.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddKeyVaultKeyProviderReadinessCheck_DefaultName_MatchesHealthCheckNamesEncryptionKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEncryptionKeyProviderProbe>());

        services.AddHealthChecks().AddKeyVaultKeyProviderReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.EncryptionKeyProvider);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
