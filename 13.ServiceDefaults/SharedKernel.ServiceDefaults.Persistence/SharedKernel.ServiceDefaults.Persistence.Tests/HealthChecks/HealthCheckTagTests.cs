using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddDatabaseReadinessCheck_RegistersWithReadyDbTags_NeverLive()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks()
            .AddDatabaseReadinessCheck<DatabaseReadinessHealthCheckTests.TestDbContext>();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Database);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Db, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddDapperDatabaseReadinessCheck_RegistersWithReadyDbTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<SharedKernel.Persistence.Abstractions.Connections.IDbConnectionFactory>());

        services.AddHealthChecks().AddDapperDatabaseReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Database);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Db, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
