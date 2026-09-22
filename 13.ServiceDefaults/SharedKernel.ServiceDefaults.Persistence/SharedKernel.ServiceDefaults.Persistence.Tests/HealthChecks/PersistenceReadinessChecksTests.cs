using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.HealthChecks;

/// <summary>F10: distinct readiness-check names, the startup-migration gate and the capability checks.</summary>
public sealed class PersistenceReadinessChecksTests
{
    [Fact]
    public void EfCoreAndDapperDatabaseChecks_CanBeRegisteredTogether()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddDatabaseReadinessCheck<DatabaseReadinessHealthCheckTests.TestDbContext>()
            .AddDapperDatabaseReadinessCheck()
            .AddPersistenceStartupReadinessCheck()
            .AddFieldEncryptionReadinessCheck()
            .AddAuditSealingReadinessCheck();

        using var provider = services.BuildServiceProvider();
        var names = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(r => r.Name).ToList();

        names.Should().OnlyHaveUniqueItems().And.Contain(
            [HealthCheckNames.Database, HealthCheckNames.DapperDatabase, HealthCheckNames.PersistenceStartup,
             HealthCheckNames.FieldEncryption, HealthCheckNames.AuditSealing]);
    }

    [Fact]
    public async Task PersistenceStartupCheck_IsUnhealthy_UntilStartupMigrationsCompleted()
    {
        var startup = Substitute.For<IPersistenceStartup>();
        startup.IsCompleted.Returns(false);
        var check = new PersistenceStartupHealthCheck(startup);

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);

        startup.IsCompleted.Returns(true);
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task DatabaseCheck_IsUnhealthy_WhileStartupMigrationsRun_WithoutTouchingTheDatabase()
    {
        var startup = Substitute.For<IPersistenceStartup>();
        startup.IsCompleted.Returns(false);

        // The context is never queried: the gate answers first.
        var check = new DatabaseReadinessHealthCheck<DatabaseReadinessHealthCheckTests.TestDbContext>(null!, startup);

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("migrations");
    }

    [Fact]
    public async Task FieldEncryptionCheck_ReportsTheKeyRingProbe()
    {
        var probe = Substitute.For<IEncryptionKeyProviderProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new EncryptionKeyProviderHealth(false, "key vault unreachable"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton(FieldEncryptionServiceKeys.KeyRingProbe, probe);
        services.AddHealthChecks().AddFieldEncryptionReadinessCheck();
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Entries[HealthCheckNames.FieldEncryption].Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries[HealthCheckNames.FieldEncryption].Description.Should().Be("key vault unreachable");
    }

    [Fact]
    public async Task AuditSealingCheck_IsDegraded_OnlyWhenTheLagExceedsTheAllowance()
    {
        var probe = Substitute.For<IAuditSealingProbe>();
        var check = new AuditSealingHealthCheck(probe, TimeSpan.FromMinutes(5));

        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new AuditSealingHealth(3, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10)));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new AuditSealingHealth(900, DateTimeOffset.UtcNow, TimeSpan.FromHours(1)));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Degraded);
    }
}
