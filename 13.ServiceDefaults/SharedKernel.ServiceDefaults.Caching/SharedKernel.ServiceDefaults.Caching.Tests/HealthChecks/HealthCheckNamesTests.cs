using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Caching.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddCacheReadinessCheck_DefaultName_MatchesHealthCheckNamesCache()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICacheService>());

        services.AddHealthChecks().AddCacheReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Cache);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
