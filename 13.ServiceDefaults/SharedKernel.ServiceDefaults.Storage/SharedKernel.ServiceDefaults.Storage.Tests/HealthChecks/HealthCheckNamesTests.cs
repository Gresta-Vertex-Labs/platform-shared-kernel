using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage;

namespace SharedKernel.ServiceDefaults.Storage.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddStorageReadinessCheck_DefaultName_MatchesHealthCheckNamesStorage()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFileStorageHealthProbe>());

        services.AddHealthChecks().AddStorageReadinessCheck("invoices");

        var registration = GetRegistration(services, HealthCheckNames.Storage);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
