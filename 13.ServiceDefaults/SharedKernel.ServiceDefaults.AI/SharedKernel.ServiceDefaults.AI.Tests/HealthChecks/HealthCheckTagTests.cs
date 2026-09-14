using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.AI.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddVectorStoreReadinessCheck_RegistersWithReadyVectorStoreTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IVectorCollectionProvisioner>());

        services.AddHealthChecks().AddVectorStoreReadinessCheck("documents");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.VectorStore);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.VectorStore, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
