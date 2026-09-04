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

public sealed class HealthCheckNamesTests
{
    [Fact]
    public void AddRedisHealthCheck_DefaultName_MatchesHealthCheckNamesRedis()
    {
        var services = new ServiceCollection();

        services.AddHealthChecks().AddRedisHealthCheck("localhost:6379");

        var registration = GetRegistration(services, HealthCheckNames.Redis);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddCacheReadinessCheck_DefaultName_MatchesHealthCheckNamesCache()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ICacheService>());

        services.AddHealthChecks().AddCacheReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Cache);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddMessagingReadinessCheck_DefaultName_MatchesHealthCheckNamesMessaging()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IMessageBusProbe>());

        services.AddHealthChecks().AddMessagingReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Messaging);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddSharedKernelHealthChecks_StartupCheck_DefaultName_MatchesHealthCheckNamesStartup()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelHealthChecks();

        var registration = GetRegistration(services, HealthCheckNames.Startup);
        Assert.NotNull(registration);
    }

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

    [Fact]
    public void AddStorageReadinessCheck_DefaultName_MatchesHealthCheckNamesStorage()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFileStorage>());

        services.AddHealthChecks().AddStorageReadinessCheck("my-bucket");

        var registration = GetRegistration(services, HealthCheckNames.Storage);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddSearchReadinessCheck_DefaultName_MatchesHealthCheckNamesSearch()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISearchIndexProvisioner>());

        services.AddHealthChecks().AddSearchReadinessCheck("products");

        var registration = GetRegistration(services, HealthCheckNames.Search);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddVectorStoreReadinessCheck_DefaultName_MatchesHealthCheckNamesVectorStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IVectorCollectionProvisioner>());

        services.AddHealthChecks().AddVectorStoreReadinessCheck("documents");

        var registration = GetRegistration(services, HealthCheckNames.VectorStore);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddWorkflowReadinessCheck_DefaultName_MatchesHealthCheckNamesWorkflows()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWorkflowServiceProbe>());

        services.AddHealthChecks().AddWorkflowReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Workflows);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddSchedulerReadinessCheck_DefaultName_MatchesHealthCheckNamesScheduler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISchedulerServiceProbe>());

        services.AddHealthChecks().AddSchedulerReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.Scheduler);
        Assert.NotNull(registration);
    }

    [Fact]
    public void AddKeyVaultKeyProviderReadinessCheck_DefaultName_MatchesHealthCheckNamesEncryptionKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IEncryptionKeyProviderProbe>());

        services.AddHealthChecks().AddKeyVaultKeyProviderReadinessCheck();

        var registration = GetRegistration(services, HealthCheckNames.EncryptionKeyProvider);
        Assert.NotNull(registration);
    }

    private static HealthCheckRegistration? GetRegistration(IServiceCollection services, string name)
    {
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        return options.Value.Registrations.SingleOrDefault(r => r.Name == name);
    }
}
