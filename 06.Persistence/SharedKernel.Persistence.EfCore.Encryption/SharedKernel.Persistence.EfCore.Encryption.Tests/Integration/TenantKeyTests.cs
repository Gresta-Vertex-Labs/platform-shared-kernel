using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>Per-tenant data keys and crypto-shredding.</summary>
[Collection("EncryptionPostgres")]
public sealed class TenantKeyTests(PostgreSqlContainerFixture fixture)
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private string Cs(string db) => EncryptionHost.Database(fixture.ConnectionString, db);

    private static ServiceProvider TenantKeyHost(string cs, TestRequestContext request) =>
        EncryptionHost.Build<CustomerDbContext>(cs, request, configure: k => k.UseTenantDataKeys<TestEnvelopeProvider>());

    private static Customer NewCustomer(Guid tenant, string email) => new()
    {
        TenantId = tenant,
        Name = "n",
        Email = email,
        Billing = new Address { City = "c", Bank = new BankAccount { Iban = "TR00 1" } },
    };

    private async Task CreateDatabaseAsync(ServiceProvider sp, string cs)
    {
        await using var scope = sp.CreateAsyncScope();
        await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        await EncryptionHost.ExecuteAsync(cs, TenantKeyStore.CreateTableSql(schema: null));
    }

    private static async Task AddAsync(ServiceProvider sp, TestRequestContext request, Guid tenant, string email)
    {
        request.TenantId = tenant;
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        context.Customers.Add(NewCustomer(tenant, email));
        await context.SaveChangesAsync();
    }

    private static async Task<List<Customer>> ReadAsync(ServiceProvider sp, TestRequestContext request, Guid tenant)
    {
        request.TenantId = tenant;
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task EachTenantsValues_AreEncryptedUnderItsOwnWrappedDataKey()
    {
        var cs = Cs("enc_t_keys");
        var request = new TestRequestContext();
        await using var sp = TenantKeyHost(cs, request);
        await CreateDatabaseAsync(sp, cs);

        await AddAsync(sp, request, _tenantA, "a@example.com");
        await AddAsync(sp, request, _tenantB, "b@example.com");

        var stored = await EncryptionHost.QueryAsync(cs, "SELECT tenant_id, email FROM customers");
        foreach (var row in stored)
        {
            EncryptedPayload.TryParse((string)row["email"]!, out var payload).Should().BeTrue();
            payload!.KeyId.Should().Be($"skt:{(Guid)row["tenant_id"]!:N}");
        }

        var keys = await EncryptionHost.QueryAsync(cs, "SELECT tenant_id, master_key_id, wrapped_key FROM sk_tenant_encryption_keys");
        keys.Should().HaveCount(2).And.OnlyContain(k => (string)k["master_key_id"]! == "master-1" && k["wrapped_key"] != null);

        (await ReadAsync(sp, request, _tenantA)).Single().Email.Should().Be("a@example.com");
        request.TenantId = _tenantB;
        await using var scope = sp.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.WhereEncryptedEquals(x => x.Email, "b@example.com").CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task ShredTenant_MakesOnlyThatTenantUnreadable_ClearsItsBlindIndexes_AndBlocksNewData()
    {
        var cs = Cs("enc_t_shred");
        var request = new TestRequestContext();
        await using var sp = TenantKeyHost(cs, request);
        await CreateDatabaseAsync(sp, cs);
        await AddAsync(sp, request, _tenantA, "a@example.com");
        await AddAsync(sp, request, _tenantB, "b@example.com");

        TenantShredResult result;
        await using (var scope = sp.CreateAsyncScope())
            result = await scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>().ShredTenantAsync(_tenantA);

        result.BlindIndexValuesCleared.Should().Be(2); // email and iban indexes of tenant A's one row

        var readA = () => ReadAsync(sp, request, _tenantA);
        await readA.Should().ThrowAsync<TenantKeyShreddedException>();
        (await ReadAsync(sp, request, _tenantB)).Single().Email.Should().Be("b@example.com");

        var rows = await EncryptionHost.QueryAsync(cs, $"SELECT email_blind_index, billing_bank_iban_blind_index FROM customers WHERE tenant_id = '{_tenantA}'");
        rows.Single().Values.Should().OnlyContain(v => v == null);
        (await EncryptionHost.QueryAsync(cs, $"SELECT wrapped_key, shredded_at FROM sk_tenant_encryption_keys WHERE tenant_id = '{_tenantA}'"))
            .Single().Should().Match<Dictionary<string, object?>>(r => r["wrapped_key"] == null && r["shredded_at"] != null);

        var write = () => AddAsync(sp, request, _tenantA, "again@example.com");
        await write.Should().ThrowAsync<TenantKeyShreddedException>();

        // A process that never saw the key (another replica) reads the tombstone.
        await using var otherProcess = TenantKeyHost(cs, request);
        var readElsewhere = () => ReadAsync(otherProcess, request, _tenantA);
        await readElsewhere.Should().ThrowAsync<TenantKeyShreddedException>();

        await using var verifyScope = sp.CreateAsyncScope();
        var verify = await verifyScope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>()
            .RunAsync(new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });
        verify.ShreddedValues.Should().Be(2);
        verify.UndecryptableValues.Should().Be(0);
    }

    [Fact]
    public async Task ReEncrypt_MovesExistingRootKeyValuesOntoTenantKeys_AndLeavesNonTenantedValuesOnTheRootKey()
    {
        var cs = Cs("enc_t_migrate");
        var request = new TestRequestContext();
        await using (var rootOnly = EncryptionHost.Build<CustomerDbContext>(cs, request))
        {
            await CreateDatabaseAsync(rootOnly, cs);
            await AddAsync(rootOnly, request, _tenantA, "a@example.com");
            await using var scope = rootOnly.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            context.Documents.Add(new Document { Id = 7, Body = "doc", Title = "t" });
            await context.SaveChangesAsync();
        }

        await using var sp = TenantKeyHost(cs, request);
        await using (var scope = sp.CreateAsyncScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>()
                .RunAsync(new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v1" });
            report.ValuesReEncrypted.Should().Be(2);
            report.ValuesByKeyId.Should().ContainKey(EncryptionMaintenanceReport.TenantDataKeysLabel).And.ContainKey("v1");
        }

        EncryptedPayload.TryParse((string)(await EncryptionHost.QueryAsync(cs, "SELECT email FROM customers")).Single()["email"]!, out var customerPayload);
        customerPayload!.KeyId.Should().StartWith("skt:");
        EncryptedPayload.TryParse((string)(await EncryptionHost.QueryAsync(cs, "SELECT body FROM documents")).Single()["body"]!, out var documentPayload);
        documentPayload!.KeyId.Should().Be("v1");
        (await ReadAsync(sp, request, _tenantA)).Single().Email.Should().Be("a@example.com");
    }
}
