using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddDatabaseReadinessCheck_DefaultName_MatchesHealthCheckNamesDatabase()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks().AddDatabaseReadinessCheck<DatabaseReadinessHealthCheckTests.TestDbContext>();

        var registration = GetRegistration(services, HealthCheckNames.Database);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddDapperDatabaseReadinessCheck_DefaultName_MatchesHealthCheckNamesDatabase()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks().AddDapperDatabaseReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Database);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
