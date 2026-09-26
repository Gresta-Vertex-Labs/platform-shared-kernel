using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Health;
using SharedKernel.Execution.Auditing;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Auditing.Sealing;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Registration;

/// <summary>The README's setup samples, compiled against the real API (they drifted to removed APIs before).</summary>
public sealed class ReadmeSampleTests
{
    public sealed class OrderDbContext(DbContextOptions<OrderDbContext> o, PersistenceContextDependencies d) : TenantedDbContext(o, d);

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:orders"] = "Host=localhost;Database=orders",
        ["ConnectionStrings:audit-sealer"] = "Host=localhost;Database=orders;Username=app_audit_sealer",
        ["SharedKernel:Persistence:audit-sealer:ConnectionStringName"] = "audit-sealer",
        ["SharedKernel:Persistence:Auditing:CurrentKeyId"] = "k1",
        ["SharedKernel:Persistence:Auditing:Keys:k1:Material"] = Support.TestKeys.K1,
        ["SharedKernel:Persistence:Auditing:Keys:k1:Order"] = "1",
        ["SharedKernel:Persistence:Auditing:Sealer:DataSourceName"] = "audit-sealer",
    }).Build();

    [Fact]
    public void SetupSample_WithASeparateSealerRole_RegistersTheLedger()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);

        services.AddSharedKernelCryptography(configuration);
        services.AddSharedKernelPostgres<OrderDbContext>(configuration, "orders", p => p
            .UseMultiTenancy(rowLevelSecurity: true)
            .UseAuditTrail());
        services.AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAuditQueryService>().Should().NotBeNull();
        provider.GetRequiredReadinessProbe(AuditSealingReadiness.ProbeName).Should().NotBeNull();
        provider.GetRequiredService<AuditSealerConnectionFactory>().IsSeparate.Should().BeTrue();
    }

    [Fact]
    public void MigrationSample_EmitsTheLedgerAndItsGrants()
    {
        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");

        migrationBuilder.CreateAuditLedgerTable(runtimeRole: "app_runtime", sealerRole: "app_audit_sealer");

        var sql = migrationBuilder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
        sql.Should().Contain(s => s.StartsWith("REVOKE ALL ON audit_records", StringComparison.Ordinal) && s.EndsWith("FROM app_runtime;", StringComparison.Ordinal));
        sql.Should().Contain("GRANT SELECT ON audit_chain_links, audit_checkpoints TO app_runtime;");
        sql.Should().Contain("GRANT SELECT, INSERT ON audit_chain_links, audit_checkpoints TO app_audit_sealer;");
        sql.Should().NotContain(s => s.Contains("UPDATE", StringComparison.Ordinal) && s.StartsWith("GRANT", StringComparison.Ordinal));
    }
}
