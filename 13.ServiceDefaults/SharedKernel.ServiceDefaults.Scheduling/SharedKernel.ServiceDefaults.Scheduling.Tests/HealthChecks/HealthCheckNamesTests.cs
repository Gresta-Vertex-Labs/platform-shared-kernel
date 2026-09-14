using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Scheduling.Probes;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Scheduling.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddSchedulerReadinessCheck_DefaultName_MatchesHealthCheckNamesScheduler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISchedulerServiceProbe>());

        services.AddHealthChecks().AddSchedulerReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Scheduler);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
