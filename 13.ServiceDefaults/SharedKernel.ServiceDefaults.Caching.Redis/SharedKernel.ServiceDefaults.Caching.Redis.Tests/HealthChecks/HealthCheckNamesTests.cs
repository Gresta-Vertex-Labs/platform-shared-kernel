using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Caching.Redis.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddRedisHealthCheck_DefaultName_MatchesHealthCheckNamesRedis()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IRedisConnectionProbe>());

        services.AddHealthChecks().AddRedisHealthCheck();

        var registration = GetRegistration(services, HealthCheckNames.Redis);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
