using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Auditing.SelfCheck;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>
/// Findings S5 (a forged link cannot stall sealing or hide the backlog; a separate sealer role), F2 (the grants
/// <c>CreateAuditLedgerTable(runtimeRole:)</c> issues agree with the self-check) and S7 (anonymous is not System).
/// </summary>
[Collection("AuditPostgres")]
public sealed class SealerHardeningTests(PostgreSqlContainerFixture fixture)
{
    private const string SealerKey = "audit-sealer";

    private static AuditEntry Failed(string resourceId, string resourceType = "Order") => new()
    {
        Action = "OrderRejected",
        ResourceType = resourceType,
        ResourceId = resourceId,
        Outcome = AuditOutcome.Failed,
    };

    private static Task<AuditRecord> WriteAsync(LedgerHost host, AuditEntry entry) =>
        host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(entry));

    private static Task<AuditSealPassResult> SealAsync(LedgerHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());

    private static async Task<(string Runtime, string Sealer)> CreateRolesAsync(string ownerConnectionString)
    {
        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var runtime = "ledger_rt_" + suffix;
        var sealer = "ledger_seal_" + suffix;
        var database = new NpgsqlConnectionStringBuilder(ownerConnectionString).Database;

        // The Npgsql README's default privileges: every table the owner creates is readable and writable by the runtime
        // role — UPDATE and DELETE included, which the ledger must not allow.
        await LedgerTestDatabase.ExecuteAsync(ownerConnectionString, $"""
            CREATE ROLE {runtime} LOGIN PASSWORD 'pw' NOSUPERUSER NOBYPASSRLS;
            CREATE ROLE {sealer} LOGIN PASSWORD 'pw' NOSUPERUSER NOBYPASSRLS;
            GRANT CONNECT ON DATABASE {database} TO {runtime}, {sealer};
            GRANT USAGE ON SCHEMA public TO {runtime}, {sealer};
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {runtime};
            """);
        return (runtime, sealer);
    }

    private static string As(string connectionString, string role) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Username = role, Password = "pw" }.ConnectionString;

    private static async Task ApplyAsync(string connectionString, Action<MigrationBuilder> migration)
    {
        var builder = new MigrationBuilder(activeProvider: "Npgsql");
        migration(builder);
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
            await LedgerTestDatabase.ExecuteAsync(connectionString, operation.Sql);
    }

    [Fact]
    public async Task ForgedLinkWithAHugeInsertXid_CannotStallSealing_OrHideTheBacklog()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using (var first = LedgerHost.Build(cs))
        {
            await WriteAsync(first, Failed("o-0"));
            (await SealAsync(first)).RecordsSealed.Should().Be(1);

            // Anyone with INSERT on the link table (the runtime role, without a separate sealer role) plants a link whose
            // record_insert_xid is beyond every real transaction. The old sealer treated the highest one as its watermark.
            var forged = await WriteAsync(first, Failed("f-1", "Forged"));
            await LedgerTestDatabase.ExecuteAsync(cs, $"""
                INSERT INTO {AuditLedgerSchema.LinksTable}
                    (record_id, tenant_id, resource_type, sequence, previous_mac, mac, key_id, algorithm, format_version, record_insert_xid, sealed_on)
                VALUES ('{forged.Id}', '{TestRequestContext.TenantA}', 'Forged', 1, NULL, '\x00', 'k1', 'HMAC-SHA256', 3, 9000000000000000000, now())
                """);

            await WriteAsync(first, Failed("o-1"));
            await WriteAsync(first, Failed("o-2"));
            (await first.Get<IAuditSealingProbe>().ProbeAsync()).UnsealedRecords.Should().Be(2);
            (await SealAsync(first)).RecordsSealed.Should().Be(2);
        }

        // A fresh process (no learned bound) sees the same: nothing hidden, nothing left.
        await using var second = LedgerHost.Build(cs);
        await WriteAsync(second, Failed("o-3"));
        (await second.Get<IAuditSealingProbe>().ProbeAsync()).UnsealedRecords.Should().Be(1);
        (await SealAsync(second)).RecordsSealed.Should().Be(1);
        (await second.Get<IAuditSealingProbe>().ProbeAsync()).UnsealedRecords.Should().Be(0);

        var order = await second.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"));
        order.IsIntact.Should().BeTrue();
        order.RecordsChecked.Should().Be(4);
        (await second.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Forged"))).IsIntact
            .Should().BeFalse("the forged link does not verify");
    }

    [Fact]
    public async Task SeparateSealerRole_WithTheMigrationsGrants_PassesTheSelfCheck_AndTheRuntimeRoleCannotForgeASeal()
    {
        var ownerCs = await LedgerTestDatabase.CreateAsync(fixture, withLedger: false);
        var (runtime, sealer) = await CreateRolesAsync(ownerCs);
        await ApplyAsync(ownerCs, m => m.CreateAuditLedgerTable(runtimeRole: runtime, sealerRole: sealer));
        var runtimeCs = As(ownerCs, runtime);

        await using var host = LedgerHost.Build(runtimeCs, o =>
        {
            o.SealerDataSourceName = SealerKey;
            o.ConfigureServices = s => s.AddKeyedSingleton<IDbConnectionFactory>(SealerKey, new TestConnectionFactory(As(ownerCs, sealer)));
        });

        (await host.Get<AuditLedgerSelfCheck>().RunAsync(CancellationToken.None)).Should().BeEmpty();

        var record = await WriteAsync(host, Failed("o-1"));
        (await SealAsync(host)).RecordsSealed.Should().Be(1);
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order"))).IsIntact.Should().BeTrue();

        var forge = () => LedgerTestDatabase.ExecuteAsync(runtimeCs, $"""
            INSERT INTO {AuditLedgerSchema.LinksTable}
                (record_id, tenant_id, resource_type, sequence, previous_mac, mac, key_id, algorithm, format_version, record_insert_xid, sealed_on)
            VALUES ('{record.Id}', NULL, 'X', 1, NULL, '\x00', 'k1', 'HMAC-SHA256', 3, 1, now())
            """);
        (await forge.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

        // Granting it back is what the self-check reports.
        await LedgerTestDatabase.ExecuteAsync(ownerCs, $"GRANT INSERT ON {AuditLedgerSchema.LinksTable} TO {runtime}");
        (await host.Get<AuditLedgerSelfCheck>().RunAsync(CancellationToken.None))
            .Should().ContainSingle().Which.Should().Contain("INSERT").And.Contain(AuditLedgerSchema.LinksTable);
    }

    [Fact]
    public async Task CreateAuditLedgerTable_WithTheRuntimeRole_RevokesWhatDefaultPrivilegesGranted()
    {
        // Finding F2: the Npgsql README's default privileges gave the runtime role UPDATE and DELETE on the ledger,
        // which the self-check then rejected; the migration now sets the ledger's privileges itself.
        var ownerCs = await LedgerTestDatabase.CreateAsync(fixture, withLedger: false);
        var (runtime, _) = await CreateRolesAsync(ownerCs);
        await ApplyAsync(ownerCs, m => m.CreateAuditLedgerTable(runtimeRole: runtime));
        var runtimeCs = As(ownerCs, runtime);

        (await new AuditLedgerSelfCheck(new TestConnectionFactory(runtimeCs)).RunAsync(CancellationToken.None)).Should().BeEmpty();

        await using var host = LedgerHost.Build(runtimeCs);
        var record = await WriteAsync(host, Failed("o-1") with { AfterSnapshot = "{}" });
        (await SealAsync(host)).RecordsSealed.Should().Be(1);
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().ErasePayloadAsync(record.Id, "gdpr"))).Should().BeTrue();
        var update = () => LedgerTestDatabase.ExecuteAsync(runtimeCs, $"UPDATE {AuditLedgerSchema.RecordsTable} SET action = 'x'");
        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Theory]
    [InlineData("app runtime")]
    [InlineData("1app")]
    [InlineData("app;DROP TABLE x")]
    public void CreateAuditLedgerTable_RejectsARoleThatIsNotAPlainIdentifier_BeforeQueuingAnything(string role)
    {
        var builder = new MigrationBuilder(activeProvider: "Npgsql");

        var act = () => builder.CreateAuditLedgerTable(runtimeRole: role);

        act.Should().Throw<ArgumentException>();
        builder.Operations.Should().BeEmpty();
    }

    [Fact]
    public void CreateAuditLedgerTable_RejectsTheSameRoleAsRuntimeAndSealer()
    {
        var act = () => new MigrationBuilder(activeProvider: "Npgsql").CreateAuditLedgerTable(runtimeRole: "app", sealerRole: "app");

        act.Should().Throw<ArgumentException>().WithMessage("*role of its own*");
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_IsRecordedAsAnonymous_NeverAsTheSystem()
    {
        // Finding S7: IRequestContext's default ActorKind (and AnonymousRequestContext) report System for an
        // unauthenticated caller; the ledger records Anonymous regardless.
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        host.Context.IsAuthenticated = false;
        host.Context.UserId = null;
        host.Context.ActorKind = ActorKind.System;

        var record = await WriteAsync(host, Failed("o-1"));

        record.ActorKind.Should().Be(ActorKind.Anonymous);
        var stored = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(new AuditRecordQuery { ResourceType = "Order" }));
        stored.Items.Single().ActorKind.Should().Be(ActorKind.Anonymous);
    }
}
