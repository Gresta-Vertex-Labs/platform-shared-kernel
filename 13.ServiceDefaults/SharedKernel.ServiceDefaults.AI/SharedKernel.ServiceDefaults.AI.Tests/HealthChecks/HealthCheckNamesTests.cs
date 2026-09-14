using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.AI.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddVectorStoreReadinessCheck_DefaultName_MatchesHealthCheckNamesVectorStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IVectorCollectionProvisioner>());

        services.AddHealthChecks().AddVectorStoreReadinessCheck("documents");

        var registration = GetRegistration(services, HealthCheckNames.VectorStore);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
