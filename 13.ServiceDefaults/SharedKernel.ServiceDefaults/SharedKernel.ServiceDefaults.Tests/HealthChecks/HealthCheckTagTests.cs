using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Scheduling.Probes;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Workflows.Temporal.Health;

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
    public void AddMessagingReadinessCheck_RegistersWithReadyMessagingTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageBusProbe>());

        services.AddHealthChecks().AddMessagingReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Messaging);

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

    [Fact]
    public void AddSearchReadinessCheck_RegistersWithReadySearchTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISearchIndexProvisioner>());

        services.AddHealthChecks().AddSearchReadinessCheck("products");

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Search);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Search, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

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

    [Fact]
    public void AddWorkflowReadinessCheck_RegistersWithReadyWorkflowsTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWorkflowServiceProbe>());

        services.AddHealthChecks().AddWorkflowReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Workflows);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Workflows, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddSchedulerReadinessCheck_RegistersWithReadySchedulerTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISchedulerServiceProbe>());

        services.AddHealthChecks().AddSchedulerReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.Scheduler);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.Scheduler, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    [Fact]
    public void AddKeyVaultKeyProviderReadinessCheck_RegistersWithReadyEncryptionKeyProviderTags_NeverLive()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEncryptionKeyProviderProbe>());

        services.AddHealthChecks().AddKeyVaultKeyProviderReadinessCheck();

        var registrations = GetRegistrations(services);
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckNames.EncryptionKeyProvider);

        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
        Assert.Contains(HealthCheckTags.EncryptionKeyProvider, registration.Tags);
        Assert.DoesNotContain(HealthCheckTags.Live, registration.Tags);
    }

    private static IEnumerable<HealthCheckRegistration> GetRegistrations(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations;
    }
}
