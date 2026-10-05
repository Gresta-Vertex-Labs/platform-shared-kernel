using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SharedKernel.Execution.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.SelfCheck;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>The startup self-check, and the least-privilege role the README documents.</summary>
[Collection("AuditPostgres")]
public sealed class SelfCheckTests(PostgreSqlContainerFixture fixture)
{
    /// <summary>The grant script from the package README.</summary>
    private static string GrantScript(string role) =>
        $"""
        GRANT SELECT, INSERT ON audit_records, audit_chain_links, audit_checkpoints TO {role};
        GRANT SELECT, INSERT, DELETE ON audit_record_payloads TO {role};
        """;

    private static async Task<string> CreateRuntimeRoleAsync(string ownerConnectionString)
    {
        var role = "ledger_app_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var builder = new NpgsqlConnectionStringBuilder(ownerConnectionString);
        await LedgerTestDatabase.ExecuteAsync(ownerConnectionString,
            $"""
            CREATE ROLE {role} LOGIN PASSWORD 'ledger-app' NOSUPERUSER NOBYPASSRLS;
            GRANT CONNECT ON DATABASE {builder.Database} TO {role};
            GRANT USAGE ON SCHEMA public TO {role};
            {GrantScript(role)}
            """);

        builder.Username = role;
        builder.Password = "ledger-app";
        return builder.ConnectionString;
    }

    private static Task<IReadOnlyList<string>> RunAsync(string cs) =>
        new AuditLedgerSelfCheck(new TestConnectionFactory(cs)).RunAsync(CancellationToken.None);

    [Fact]
    public async Task Superuser_Owner_IsReported()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);

        var findings = await RunAsync(cs);

        findings.Should().Contain(f => f.Contains("superuser"));
        findings.Should().Contain(f => f.Contains("owns") && f.Contains(AuditLedgerSchema.RecordsTable));
    }

    [Fact]
    public async Task LeastPrivilegeRole_PassesTheCheck_AndRunsTheWholeLedger()
    {
        var ownerCs = await LedgerTestDatabase.CreateAsync(fixture);
        var appCs = await CreateRuntimeRoleAsync(ownerCs);

        (await RunAsync(appCs)).Should().BeEmpty();

        await using var host = LedgerHost.Build(appCs);
        var record = await host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(new AuditEntry
        {
            Action = "OrderRejected", ResourceType = "Order", ResourceId = "o-1", Outcome = AuditOutcome.Failed, AfterSnapshot = "{}",
        }));
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync())).RecordsSealed.Should().Be(1);
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().ErasePayloadAsync(record.Id, "gdpr"))).Should().BeTrue();
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"))).IsIntact.Should().BeTrue();

        var tamper = () => LedgerTestDatabase.ExecuteAsync(appCs, $"ALTER TABLE {AuditLedgerSchema.RecordsTable} DISABLE TRIGGER USER");
        await tamper.Should().ThrowAsync<PostgresException>("the runtime role does not own the table");
        var update = () => LedgerTestDatabase.ExecuteAsync(appCs, $"UPDATE {AuditLedgerSchema.LinksTable} SET key_id = 'x'");
        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task ExtraPrivilege_MissingTrigger_AndNonAlwaysTrigger_AreReported()
    {
        var ownerCs = await LedgerTestDatabase.CreateAsync(fixture);
        var appCs = await CreateRuntimeRoleAsync(ownerCs);
        var role = new NpgsqlConnectionStringBuilder(appCs).Username;

        await LedgerTestDatabase.ExecuteAsync(ownerCs,
            $"""
            GRANT UPDATE ON {AuditLedgerSchema.RecordsTable} TO {role};
            GRANT DELETE ON {AuditLedgerSchema.LinksTable} TO {role};
            DROP TRIGGER {AuditLedgerSchema.CheckpointsTable}_reject_delete ON {AuditLedgerSchema.CheckpointsTable};
            ALTER TABLE {AuditLedgerSchema.PayloadsTable} ENABLE TRIGGER {AuditLedgerSchema.PayloadsTable}_reject_update;
            ALTER TABLE {AuditLedgerSchema.RecordsTable} ENABLE ROW LEVEL SECURITY;
            """);

        var findings = await RunAsync(appCs);

        findings.Should().Contain(f => f.Contains("UPDATE") && f.Contains(AuditLedgerSchema.RecordsTable));
        findings.Should().Contain(f => f.Contains("DELETE") && f.Contains(AuditLedgerSchema.LinksTable));
        findings.Should().Contain(f => f.Contains("audit_checkpoints_reject_delete") && f.Contains("missing"));
        findings.Should().Contain(f => f.Contains("audit_record_payloads_reject_update") && f.Contains("ENABLE ALWAYS"));
        findings.Should().Contain(f => f.Contains("row-level security") && f.Contains(AuditLedgerSchema.RecordsTable));
        findings.Should().HaveCount(5);
    }

    [Fact]
    public async Task MissingTables_AreReported()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture, withLedger: false);

        (await RunAsync(cs)).Should().Contain(f => f.Contains("does not exist") && f.Contains("CreateAuditLedgerTable"));
    }

    [Theory]
    [InlineData(AuditSelfCheckMode.Fail, true)]
    [InlineData(AuditSelfCheckMode.Warn, false)]
    [InlineData(AuditSelfCheckMode.Off, false)]
    public async Task HostedCheck_FailsStartupOnlyInFailMode(AuditSelfCheckMode mode, bool throws)
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        var service = new AuditLedgerSelfCheckHostedService(
            new AuditLedgerSelfCheck(new TestConnectionFactory(cs)),
            Microsoft.Extensions.Options.Options.Create(new AuditLedgerOptions { SelfCheck = mode }),
            NullLogger<AuditLedgerSelfCheckHostedService>.Instance);

        // Runs once every hosted service has started (after startup migrations), not in StartAsync.
        var start = () => service.StartedAsync(CancellationToken.None);

        if (throws)
            await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*self-check failed*");
        else
            await start.Should().NotThrowAsync();
    }
}
