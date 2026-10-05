using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddSharedKernelHealthChecks_StartupCheck_RegistersWithReadyTagOnly()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelHealthChecks();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == "startup");

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
