using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Scheduling.Probes;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Scheduling.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddSchedulerReadinessCheck_RegistersWithReadySchedulerTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISchedulerServiceProbe>());

        services.AddHealthChecks().AddSchedulerReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Scheduler);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Scheduler, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
