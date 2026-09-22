using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage;

namespace SharedKernel.ServiceDefaults.Storage.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddStorageReadinessCheck_RegistersWithReadyStorageTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFileStorageHealthProbe>());

        services.AddHealthChecks().AddStorageReadinessCheck("invoices");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Storage);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Storage, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
