using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Search.Tests.HealthChecks;

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddSearchReadinessCheck_DefaultName_IsTheSearchPrefixSuffixedWithTheIndex()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISearchIndexProvisioner>());

        services.AddHealthChecks().AddSearchReadinessCheck("products");

        var registration = GetRegistration(services, $"{HealthCheckNames.Search}-products");
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddSearchReadinessCheck_CalledOncePerIndex_RegistersDistinctChecks()
    {
        // The defect this test exists for: the default name used to be the bare HealthCheckNames.Search
        // constant, so a service with two indexes — which is exactly what the indexName parameter
        // invites — registered two checks called "search" and threw
        // "Duplicate health checks were registered with the name(s): search" at startup, from
        // MapDefaultHealthCheckEndpoints(). Found by the CatalogApi sample, which registers one index
        // per engine.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISearchIndexProvisioner>());

        services.AddHealthChecks()
            .AddSearchReadinessCheck("products")
            .AddSearchReadinessCheck("order-lines-read");

        using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        Assert.Equal(2, registrations.Count);
        Assert.Contains(registrations, r => r.Name == $"{HealthCheckNames.Search}-products");
        Assert.Contains(registrations, r => r.Name == $"{HealthCheckNames.Search}-order-lines-read");

        // Resolving the health check service is what validates the registrations — and what used to
        // throw. It must not.
        var exception = Record.Exception(() => provider.GetRequiredService<HealthCheckService>());
        Assert.Null(exception);
    }

    [Fact]
    public void AddSearchReadinessCheck_ExplicitName_Wins()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISearchIndexProvisioner>());

        services.AddHealthChecks().AddSearchReadinessCheck("products", "catalogue-search");

        Assert.NotNull(GetRegistration(services, "catalogue-search"));
        Assert.Null(GetRegistration(services, $"{HealthCheckNames.Search}-products"));
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
