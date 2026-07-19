using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.Tests.HealthChecks;

public sealed class HealthCheckTagTests
{
    [Fact]
    public void AddRedisHealthCheck_RegistersWithReadyRedisCacheTags_NeverLive()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks().AddRedisHealthCheck("localhost:6379");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == "redis");

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Redis, registration.Tags);
        Assert.Contains(HealthCheckTags.Cache, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddCacheReadinessCheck_RegistersWithReadyCacheTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICacheService>());

        services.AddHealthChecks().AddCacheReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == "cache");

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Cache, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddRabbitMqMessagingHealthCheck_RegistersWithReadyMessagingTags_NeverLive()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks().AddRabbitMqMessagingHealthCheck("amqp://localhost");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == "rabbitmq");

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Messaging, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddAzureServiceBusMessagingHealthCheck_RegistersWithReadyMessagingTags_NeverLive()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks().AddAzureServiceBusMessagingHealthCheck("my-namespace.servicebus.windows.net");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == "azure-service-bus");

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Messaging, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddSharedKernelHealthChecks_StartupCheck_RegistersWithReadyTagOnly()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelHealthChecks();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == "startup");

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

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

    [Fact]
    public void AddStorageReadinessCheck_RegistersWithReadyStorageTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFileStorage>());

        services.AddHealthChecks().AddStorageReadinessCheck("my-bucket");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Storage);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Storage, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
