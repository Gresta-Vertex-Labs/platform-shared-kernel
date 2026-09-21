using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>The maintenance job: rotation, plaintext migration, blind-index recomputation, verification, RLS (finding A12).</summary>
[Collection("EncryptionPostgres")]
public sealed class MaintenanceTests(PostgreSqlContainerFixture fixture)
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private string Cs(string db) => EncryptionHost.Database(fixture.ConnectionString, db);

    private async Task SeedCustomersAsync(string cs, int perTenant = 2)
    {
        var request = new TestRequestContext();
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, request);
        await using (var scope = sp.CreateAsyncScope())
            await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);

        foreach (var tenant in new[] { _tenantA, _tenantB })
        {
            request.TenantId = tenant;
            await using var scope = sp.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            for (var i = 0; i < perTenant; i++)
            {
                context.Customers.Add(new Customer
                {
                    TenantId = tenant,
                    Name = $"c{i}",
                    Email = $"user{i}@{tenant:N}.example",
                    Note = i == 0 ? null : "note",
                    Billing = new Address { City = "x", Bank = new BankAccount { Iban = $"TR{i} 0001" } },
                });
            }

            await context.SaveChangesAsync();
        }
    }

    private static async Task<IReadOnlyList<string>> KeyIdsAsync(string cs, string sql) =>
        [.. (await EncryptionHost.QueryAsync(cs, sql)).SelectMany(r => r.Values).OfType<string>()
            .Select(v => EncryptedPayload.TryParse(v, out var p) ? p.KeyId : "(plaintext)")];

    private static async Task<EncryptionMaintenanceReport> RunAsync(
        ServiceProvider sp, EncryptionMaintenanceRequest request, IProgress<EncryptionMaintenanceProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>().RunAsync(request, progress, cancellationToken);
    }

    [Fact]
    public async Task ReEncrypt_MovesEveryTenantsValuesToTheCurrentKey_WithoutTouchingBlindIndexes()
    {
        var cs = Cs("enc_m_rotate");
        await SeedCustomersAsync(cs);
        var indexesBefore = await EncryptionHost.QueryAsync(cs, "SELECT email_blind_index FROM customers ORDER BY id");

        await using var v2 = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenantA }, currentKeyId: "v2");
        var report = await RunAsync(v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2" });

        report.Completed.Should().BeTrue();
        report.ValuesReEncrypted.Should().Be(10); // 4 emails, 2 notes, 4 ibans
        report.ValuesByKeyId.Should().Equal(new Dictionary<string, long> { ["v2"] = 10 });
        (await KeyIdsAsync(cs, "SELECT email, note, billing_bank_iban FROM customers")).Should().OnlyContain(k => k == "v2");
        (await EncryptionHost.QueryAsync(cs, "SELECT email_blind_index FROM customers ORDER BY id")).Should().BeEquivalentTo(indexesBefore);

        var verify = await RunAsync(v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });
        verify.IsSafeToRetire("v1").Should().BeTrue();
        verify.RowsScanned.Should().Be(10);

        await using var scope = v2.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.WhereEncryptedEquals(x => x.Email, $"user1@{_tenantA:N}.example").SingleAsync())
            .Note.Should().Be("note");
    }

    [Fact]
    public async Task VerifyOnly_BeforeRotation_ReportsTheOldKeyAsInUse()
    {
        var cs = Cs("enc_m_verify");
        await SeedCustomersAsync(cs, perTenant: 1);
        await using var v2 = EncryptionHost.Build<CustomerDbContext>(cs, currentKeyId: "v2");

        var report = await RunAsync(v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });

        report.IsSafeToRetire("v1").Should().BeFalse();
        report.ValuesByKeyId["v1"].Should().Be(4);
        (await KeyIdsAsync(cs, "SELECT email FROM customers")).Should().OnlyContain(k => k == "v1");
    }

    [Fact]
    public async Task ReEncrypt_WithAnUnexpectedCurrentKey_WritesNothing()
    {
        var cs = Cs("enc_m_expected");
        await SeedCustomersAsync(cs, perTenant: 1);
        await using var v2 = EncryptionHost.Build<CustomerDbContext>(cs, currentKeyId: "v2");

        var act = () => RunAsync(v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v3" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'v2'*'v3'*");
        (await KeyIdsAsync(cs, "SELECT email FROM customers")).Should().OnlyContain(k => k == "v1");
    }

    [Fact]
    public async Task CancelledRun_ReturnsACheckpoint_AndTheResumedRunCompletes()
    {
        var cs = Cs("enc_m_resume");
        await SeedCustomersAsync(cs, perTenant: 3);
        await using var v2 = EncryptionHost.Build<CustomerDbContext>(cs, currentKeyId: "v2");
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress(p =>
        {
            if (p.RowsScanned >= 2)
                cancellation.Cancel();
        });

        var first = await RunAsync(
            v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2", BatchSize = 2 }, progress, cancellation.Token);
        first.Completed.Should().BeFalse();
        first.CheckpointToken.Should().NotBeNull();

        var second = await RunAsync(
            v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2", BatchSize = 2, CheckpointToken = first.CheckpointToken });

        second.Completed.Should().BeTrue();
        (first.ValuesReEncrypted + second.ValuesReEncrypted).Should().Be(16);
        (await KeyIdsAsync(cs, "SELECT email, note, billing_bank_iban FROM customers")).Should().OnlyContain(k => k == "v2");
    }

    [Fact]
    public async Task EncryptPlaintext_MigratesAColumnThatHeldPlaintext_IncludingItsBlindIndex()
    {
        var cs = Cs("enc_m_plaintext");
        await SeedCustomersAsync(cs, perTenant: 1);
        await EncryptionHost.ExecuteAsync(cs, $"UPDATE customers SET email = 'legacy@example.com', email_blind_index = NULL WHERE tenant_id = '{_tenantA}'");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenantA });

        var before = await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });
        before.PlaintextValues.Should().Be(1);
        before.ValuesByKeyId["(plaintext)"].Should().Be(1);

        var report = await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.EncryptPlaintext, ExpectedCurrentKeyId = "v1" });
        report.ValuesEncryptedFromPlaintext.Should().Be(1);
        report.BlindIndexesRecomputed.Should().Be(1);

        await using var scope = sp.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.WhereEncryptedEquals(x => x.Email, "LEGACY@example.com").SingleAsync())
            .Email.Should().Be("legacy@example.com");
    }

    [Fact]
    public async Task BlindIndexKeyRotation_LookupsMatchBothVersions_UntilRecomputeMovesEveryIndex()
    {
        var cs = Cs("enc_m_blindindex");
        await SeedCustomersAsync(cs, perTenant: 1);
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenantA }, blindIndexVersion: "v2");

        await using (var scope = sp.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.WhereEncryptedEquals(x => x.Email, $"user0@{_tenantA:N}.example").CountAsync())
                .Should().Be(1, "rows still indexed under v1 are found while v1 is configured");
        }

        (await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly })).StaleBlindIndexes.Should().Be(4);
        var report = await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.RecomputeBlindIndexes });
        report.BlindIndexesRecomputed.Should().Be(4);
        report.ValuesReEncrypted.Should().Be(0);
        (await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly })).StaleBlindIndexes.Should().Be(0);

        (await EncryptionHost.QueryAsync(cs, "SELECT email_blind_index, billing_bank_iban_blind_index FROM customers"))
            .SelectMany(r => r.Values).OfType<string>().Should().OnlyContain(v => v.StartsWith("v2:"));
    }

    [Fact]
    public async Task TphAndTptColumns_AreEachProcessedOnce_InTheTableThatHoldsThem()
    {
        var cs = Cs("enc_m_hierarchy");
        var request = new TestRequestContext { TenantId = _tenantA };
        await using (var sp = EncryptionHost.Build<ZooDbContext>(cs, request))
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<ZooDbContext>(scope);
            context.Animals.AddRange(new Dog { TenantId = _tenantA, ChipCode = "d-1" }, new Cat { TenantId = _tenantA, ChipCode = "c-1" });
            context.Vehicles.AddRange(new Vehicle { TenantId = _tenantA, Vin = "vin-1" }, new Truck { TenantId = _tenantA, Vin = "vin-2", Permit = "P-9" });
            await context.SaveChangesAsync();
        }

        await using var v2 = EncryptionHost.Build<ZooDbContext>(cs, request, currentKeyId: "v2");
        var report = await RunAsync(v2, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2" });

        report.RowsScanned.Should().Be(5);
        report.ValuesReEncrypted.Should().Be(5);
        await using var readScope = v2.CreateAsyncScope();
        var zoo = readScope.ServiceProvider.GetRequiredService<ZooDbContext>();
        (await zoo.Animals.AsNoTracking().ToListAsync()).Select(a => a is Dog d ? d.ChipCode : ((Cat)a).ChipCode)
            .Should().BeEquivalentTo("d-1", "c-1");
        var truck = await zoo.Vehicles.OfType<Truck>().AsNoTracking().SingleAsync();
        (truck.Vin, truck.Permit).Should().Be(("vin-2", "P-9"));
        (await zoo.Set<Truck>().WhereEncryptedEquals(x => x.Permit, "p-9").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RowLevelSecurityHidingRows_FailsTheRun_InsteadOfReportingCompletion()
    {
        var cs = Cs("enc_m_rls");
        await SeedCustomersAsync(cs, perTenant: 1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await EncryptionHost.ExecuteAsync(cs, $"""
            CREATE ROLE enc_app_{suffix} LOGIN PASSWORD 'pw' NOSUPERUSER NOBYPASSRLS;
            CREATE ROLE enc_maint_{suffix} LOGIN PASSWORD 'pw' NOSUPERUSER BYPASSRLS;
            GRANT SELECT, UPDATE ON ALL TABLES IN SCHEMA public TO enc_app_{suffix}, enc_maint_{suffix};
            ALTER TABLE customers ENABLE ROW LEVEL SECURITY;
            ALTER TABLE customers FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON customers USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ANALYZE customers;
            """);

        string RoleCs(string role) => new NpgsqlConnectionStringBuilder(cs) { Username = role, Password = "pw" }.ConnectionString;
        await using var appSource = NpgsqlDataSource.Create(RoleCs($"enc_app_{suffix}"));
        await using var maintSource = NpgsqlDataSource.Create(RoleCs($"enc_maint_{suffix}"));

        // Finding A12: the old job saw 0 rows under RLS and reported Completed with nothing left on the old key.
        await using (var strict = EncryptionHost.Build<CustomerDbContext>(cs, currentKeyId: "v2", configure: k => k.UseMaintenanceDataSource(_ => appSource)))
        {
            var act = () => RunAsync(strict, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2" });
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*row-level security*BYPASSRLS*");
        }

        await using (var lax = EncryptionHost.Build<CustomerDbContext>(
            cs, currentKeyId: "v2", configure: k => k.UseMaintenanceDataSource(_ => appSource).Configure(o => o.RequireRowSecurityBypass = false)))
        {
            var act = () => RunAsync(lax, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*saw no rows*");
        }

        // The cross-tenant data source AddSharedKernelNpgsql registers for RLS is picked up without configuration.
        await using var bypass = EncryptionHost.Build<CustomerDbContext>(
            cs, currentKeyId: "v2", configureServices: s => s.AddKeyedSingleton(SharedKernel.Persistence.Npgsql.Connections.NpgsqlDataSourceKeys.CrossTenant, maintSource));
        var report = await RunAsync(bypass, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2" });
        report.ValuesReEncrypted.Should().Be(4); // both tenants: an email and an iban each
        (await KeyIdsAsync(cs, "SELECT email, billing_bank_iban FROM customers")).Should().OnlyContain(k => k == "v2");
    }

    private sealed class SynchronousProgress(Action<EncryptionMaintenanceProgress> report) : IProgress<EncryptionMaintenanceProgress>
    {
        public void Report(EncryptionMaintenanceProgress value) => report(value);
    }
}
