using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Workflows.Temporal.Health;

namespace SharedKernel.ServiceDefaults.Workflows.Temporal.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddWorkflowReadinessCheck_DefaultName_MatchesHealthCheckNamesWorkflows()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWorkflowServiceProbe>());

        services.AddHealthChecks().AddWorkflowReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Workflows);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
