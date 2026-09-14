using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddSharedKernelHealthChecks_StartupCheck_DefaultName_MatchesHealthCheckNamesStartup()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelHealthChecks();

        var registration = GetRegistration(services, HealthCheckNames.Startup);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
