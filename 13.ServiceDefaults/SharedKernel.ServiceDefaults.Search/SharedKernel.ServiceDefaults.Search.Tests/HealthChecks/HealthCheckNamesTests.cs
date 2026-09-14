using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Search.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddSearchReadinessCheck_DefaultName_MatchesHealthCheckNamesSearch()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISearchIndexProvisioner>());

        services.AddHealthChecks().AddSearchReadinessCheck("products");

        var registration = GetRegistration(services, HealthCheckNames.Search);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
