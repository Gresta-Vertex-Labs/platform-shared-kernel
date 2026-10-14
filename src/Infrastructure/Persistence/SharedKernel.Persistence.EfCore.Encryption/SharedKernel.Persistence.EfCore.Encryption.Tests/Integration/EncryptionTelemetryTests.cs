using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>
/// The field-encryption instruments, recorded through <see cref="MetricRecorder"/>: all on the one persistence meter
/// (<c>SharedKernel.Persistence</c>), named <c>sharedkernel.persistence.encryption.*</c>, each with a unit and only
/// closed-set tags (no tenant, key or value).
/// </summary>
[Collection("EncryptionPostgres")]
public sealed class EncryptionTelemetryTests(PostgreSqlContainerFixture fixture)
{
    private const string MaintenanceWritten = "sharedkernel.persistence.encryption.maintenance_rows_written";
    private const string KeysShredded = "sharedkernel.persistence.encryption.tenant_keys_shredded";

    private readonly TenantId _tenantA = new(Guid.NewGuid());
    private readonly TenantId _tenantB = new(Guid.NewGuid());

    private string Cs(string db) => EncryptionHost.Database(fixture.ConnectionString, db);

    private static Customer NewCustomer(TenantId tenant, string email) => new()
    {
        TenantId = tenant,
        Name = "n",
        Email = email,
        Billing = new Address { City = "c", Bank = new BankAccount { Iban = "TR00 1" } },
    };

    private static async Task AddAsync(ServiceProvider sp, TestRequestContext request, TenantId tenant, string email)
    {
        request.TenantId = tenant;
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        context.Customers.Add(NewCustomer(tenant, email));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ReEncrypt_CountsTheWrittenValues_ByOperation_WithClosedSetTagsOnly()
    {
        var cs = Cs("enc_tel_rotate");
        var request = new TestRequestContext();
        await using (var sp = EncryptionHost.Build<CustomerDbContext>(cs, request))
        {
            await using (var scope = sp.CreateAsyncScope())
                await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            await AddAsync(sp, request, _tenantA, "a@example.com");
            await AddAsync(sp, request, _tenantB, "b@example.com");
        }

        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);
        await using var v2 = EncryptionHost.Build<CustomerDbContext>(cs, request, currentKeyId: "v2");
        await using (var scope = v2.CreateAsyncScope())
        {
            using var crossTenant = scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Enter("test: encryption telemetry");
            var report = await scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>()
                .RunAsync(new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v2" });
            report.ValuesReEncrypted.Should().BeGreaterThan(0);

            metrics
                .ShouldHaveInstrument(MaintenanceWritten, unit: "{row}", kind: InstrumentKind.Counter)
                .ShouldHaveSum(
                    MaintenanceWritten,
                    report.ValuesReEncrypted,
                    new KeyValuePair<string, object?>("persistence.encryption.maintenance_operation", "re_encrypt"))
                .ShouldHaveTagSet(MaintenanceWritten, "persistence.encryption.maintenance_operation")
                .ShouldNotHaveTagKey(MaintenanceWritten, "tenant.id")
                .ShouldFollowMetricConvention();
        }
    }

    [Fact]
    public async Task ShreddingATenant_CountsOneShreddedKey()
    {
        var cs = Cs("enc_tel_shred");
        var request = new TestRequestContext();
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, request, configure: k => k.UseTenantDataKeys<TestEnvelopeProvider>());
        await using (var scope = sp.CreateAsyncScope())
        {
            await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            await EncryptionHost.ExecuteAsync(cs, TenantKeyStore.CreateTableSql(schema: null));
        }

        await AddAsync(sp, request, _tenantA, "a@example.com");
        using var metrics = MetricRecorder.StartRecording(PersistenceTelemetry.Name);

        await using (var scope = sp.CreateAsyncScope())
        {
            using var crossTenant = scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Enter("test: erase tenant");
            await scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>().ShredTenantAsync(_tenantA);
        }

        metrics
            .ShouldHaveInstrument(KeysShredded, unit: "{key}", kind: InstrumentKind.Counter)
            .ShouldHaveSum(KeysShredded, 1)
            .ShouldFollowMetricConvention();
    }
}
