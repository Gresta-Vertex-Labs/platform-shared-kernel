using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Auditing;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

/// <summary>GDPR/KVKK payload erasure and post-compromise resealing.</summary>
[Collection("AuditPostgres")]
public sealed class ErasureAndMaintenanceTests(PostgreSqlContainerFixture fixture)
{
    private static Task<AuditRecord> WriteAsync(LedgerHost host, string resourceId, string resourceType = "Customer") =>
        host.InScopeAsync(sp => sp.GetRequiredService<EfAuditTrailWriter>().RecordAsync(new AuditEntry
        {
            Action = "CustomerUpdated",
            ResourceType = resourceType,
            ResourceId = resourceId,
            Outcome = AuditOutcome.Failed,
            BeforeSnapshot = "{\"email\":\"jane@example.com\"}",
            AfterSnapshot = "{\"email\":\"jane.doe@example.com\"}",
        }));

    private static Task SealAsync(LedgerHost host) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IAuditLedgerMaintenance>().SealPendingAsync());

    private static Task<T> MaintenanceAsync<T>(LedgerHost host, Func<IAuditLedgerMaintenance, Task<T>> action) =>
        host.InScopeAsync(sp => action(sp.GetRequiredService<IAuditLedgerMaintenance>()));

    [Fact]
    public async Task ErasePayload_KeepsTheChainVerifiable_AndReportsTheErasure()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var target = await WriteAsync(host, "cust-1");
        await WriteAsync(host, "cust-2");
        await SealAsync(host);

        (await MaintenanceAsync(host, m => m.ErasePayloadAsync(target.Id, "GDPR request 17"))).Should().BeTrue();
        (await MaintenanceAsync(host, m => m.ErasePayloadAsync(target.Id, "GDPR request 17"))).Should().BeFalse("already erased");
        await SealAsync(host);

        var verification = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Customer"));
        verification.Should().Match<AuditChainVerificationResult>(r => r.IsIntact && r.RecordsChecked == 2 && r.ErasedPayloads == 1);

        var strict = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Customer", requirePayloads: true));
        strict.Should().Match<AuditChainVerificationResult>(r =>
            r.Status == AuditVerificationStatus.Unverifiable && r.FailureKind == AuditVerificationFailureKind.PayloadErased && r.FailedAtSequence == 1);

        var record = await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(target.Id));
        record!.Should().Match<AuditRecordVerificationResult>(r => r.IsIntact && r.PayloadErased);
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyRecordAsync(target.Id, requirePayload: true)))!
            .FailureKind.Should().Be(AuditVerificationFailureKind.PayloadErased);

        var read = (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(
            new AuditRecordQuery { ResourceType = "Customer", ResourceId = "cust-1" }))).Items.Single();
        read.Should().Match<AuditRecord>(r => r.PayloadErased && r.BeforeSnapshot == null && r.AfterSnapshot == null);

        var erasureAudit = (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().QueryAsync(
            new AuditRecordQuery { ResourceType = AuditLedgerActions.LedgerResourceType }))).Items.Single();
        erasureAudit.Action.Should().Be(AuditLedgerActions.PayloadErased);
        erasureAudit.ResourceId.Should().Be(target.Id.ToString("D"));
        erasureAudit.AfterSnapshot.Should().Contain("GDPR request 17");
    }

    [Fact]
    public async Task EraseResourcePayloads_ErasesEveryRecordOfTheResource_InTheCallersTenantOnly()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await WriteAsync(host, "cust-1");
        await WriteAsync(host, "cust-1");
        await WriteAsync(host, "cust-2");
        host.Context.TenantId = TestRequestContext.TenantB;
        await WriteAsync(host, "cust-1");
        host.Context.TenantId = TestRequestContext.TenantA;

        (await MaintenanceAsync(host, m => m.EraseResourcePayloadsAsync("Customer", "cust-1", "KVKK request 3"))).Should().Be(2);

        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {AuditLedgerSchema.PayloadsTable}"))
            .Should().Be(3, "cust-2, the other tenant's cust-1 and the erasure's own record keep their payloads");
    }

    [Fact]
    public async Task ErasePayload_OfAnotherTenantsRecord_IsNotFound_WithoutACrossTenantScope()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        var target = await WriteAsync(host, "cust-1");
        host.Context.TenantId = TestRequestContext.TenantB;

        await host.Invoking(h => MaintenanceAsync(h, m => m.ErasePayloadAsync(target.Id, "x"))).Should().ThrowAsync<KeyNotFoundException>();

        using (host.Scope.Enter("audit test"))
            (await MaintenanceAsync(host, m => m.ErasePayloadAsync(target.Id, "x"))).Should().BeTrue();
    }

    [Fact]
    public async Task PayloadsTable_AllowsOnlyDelete()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using var host = LedgerHost.Build(cs);
        await WriteAsync(host, "cust-1");

        var update = () => LedgerTestDatabase.ExecuteAsync(cs, $"UPDATE {AuditLedgerSchema.PayloadsTable} SET after_snapshot = 'x'");
        var truncate = () => LedgerTestDatabase.ExecuteAsync(cs, $"TRUNCATE {AuditLedgerSchema.PayloadsTable}");
        var deleteRecord = () => LedgerTestDatabase.ExecuteAsync(cs, $"DELETE FROM {AuditLedgerSchema.RecordsTable}");
        await update.Should().ThrowAsync<global::Npgsql.PostgresException>().WithMessage("*append-only*");
        await truncate.Should().ThrowAsync<global::Npgsql.PostgresException>().WithMessage("*append-only*");
        await deleteRecord.Should().ThrowAsync<global::Npgsql.PostgresException>().WithMessage("*append-only*");
    }

    [Fact]
    public async Task SealAllChains_AfterAKeyCompromise_MakesEveryLaterOldKeyForgeryAKeyRegression()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture);
        await using (var before = LedgerHost.Build(cs))
        {
            await WriteAsync(before, "cust-1");
            await WriteAsync(before, "o-1", "Order");
            await SealAsync(before);
        }

        using var signingKeys = new InMemorySigningKeyProvider([SigningKey.FromECDsa("cp", ECDsa.Create(ECCurve.NamedCurves.nistP256))]);
        await using var host = LedgerHost.Build(cs, o =>
        {
            o.Keys["k2"] = (TestKeys.K2, 2);
            o.CurrentKeyId = "k2";
            o.CheckpointSigningKeyId = "cp";
            o.SigningKeys = signingKeys;
        });

        await host.Invoking(h => MaintenanceAsync(h, m => m.SealAllChainsAsync("k1 leaked"))).Should().ThrowAsync<InvalidOperationException>();

        AuditResealResult result;
        using (host.Scope.Enter("audit test"))
            result = await MaintenanceAsync(host, m => m.SealAllChainsAsync("k1 leaked"));

        result.Should().Be(new AuditResealResult(ChainsResealed: 2, RecordsSealed: 2, CheckpointsEmitted: 2, FullySealed: true));
        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {AuditLedgerSchema.LinksTable} WHERE key_id = 'k2'")).Should().Be(2);

        await TamperDetectionTests.ForgeAppendAsync(cs, sequence: 3, keyId: "k1", key: Convert.FromBase64String(TestKeys.K1));
        (await host.InScopeAsync(sp => sp.GetRequiredService<IAuditQueryService>().VerifyChainAsync("Order")))
            .FailureKind.Should().Be(AuditVerificationFailureKind.KeyRegression);
    }
}
