using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Workflows.Temporal.Health;

namespace SharedKernel.ServiceDefaults.Workflows.Temporal.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddWorkflowReadinessCheck_RegistersWithReadyWorkflowsTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWorkflowServiceProbe>());

        services.AddHealthChecks().AddWorkflowReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Workflows);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Workflows, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
