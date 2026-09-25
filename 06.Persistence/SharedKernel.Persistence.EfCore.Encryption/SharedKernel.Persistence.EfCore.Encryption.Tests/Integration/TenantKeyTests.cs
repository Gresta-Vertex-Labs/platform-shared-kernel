using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>Per-tenant data keys and crypto-shredding (findings S2, S3).</summary>
[Collection("EncryptionPostgres")]
public sealed class TenantKeyTests(PostgreSqlContainerFixture fixture)
{
    private readonly TenantId _tenantA = new TenantId(Guid.NewGuid());
    private readonly TenantId _tenantB = new TenantId(Guid.NewGuid());

    private string Cs(string db) => EncryptionHost.Database(fixture.ConnectionString, db);

    private static ServiceProvider TenantKeyHost(string cs, TestRequestContext request) =>
        EncryptionHost.Build<CustomerDbContext>(cs, request, configure: k => k.UseTenantDataKeys<TestEnvelopeProvider>());

    private static Customer NewCustomer(TenantId tenant, string email) => new()
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

    private static async Task AddAsync(ServiceProvider sp, TestRequestContext request, TenantId tenant, string email)
    {
        request.TenantId = tenant;
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        context.Customers.Add(NewCustomer(tenant, email));
        await context.SaveChangesAsync();
    }

    private static async Task<List<Customer>> ReadAsync(ServiceProvider sp, TestRequestContext request, TenantId tenant)
    {
        request.TenantId = tenant;
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().ToListAsync();
    }

    private static async Task<Customer> ReadOneAsync(ServiceProvider sp, TestRequestContext request, TenantId tenant, Guid id)
    {
        request.TenantId = tenant;
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    /// <summary>Shreds inside a cross-tenant scope entered by the caller, as the API requires.</summary>
    private static async Task<TenantShredResult> ShredAsync(ServiceProvider sp, TenantId tenant, TenantShredOptions? options = null)
    {
        await using var scope = sp.CreateAsyncScope();
        using var crossTenant = scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Enter("test: erase tenant");
        return await scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>().ShredTenantAsync(tenant, options);
    }

    private static async Task<EncryptionMaintenanceReport> RunAsync(ServiceProvider sp, EncryptionMaintenanceRequest request)
    {
        await using var scope = sp.CreateAsyncScope();
        using var crossTenant = scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().Enter("test: encryption maintenance");
        return await scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>().RunAsync(request);
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

        var result = await ShredAsync(sp, _tenantA);

        result.BlindIndexValuesCleared.Should().Be(2); // email and iban indexes of tenant A's one row
        result.IsComplete.Should().BeTrue();

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

        var verify = await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });
        verify.ShreddedValues.Should().Be(2);
        verify.ShreddedTenantValuesNotErased.Should().Be(0);
        verify.UndecryptableValues.Should().Be(0);
    }

    [Fact]
    public async Task ShredTenant_WithoutAnActiveCrossTenantScope_IsRefused_AndChangesNothing()
    {
        // Finding S2: shredding used to enter the cross-tenant scope itself.
        var cs = Cs("enc_t_scope");
        var request = new TestRequestContext();
        await using var sp = TenantKeyHost(cs, request);
        await CreateDatabaseAsync(sp, cs);
        await AddAsync(sp, request, _tenantA, "a@example.com");

        await using (var scope = sp.CreateAsyncScope())
        {
            var act = () => scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>().ShredTenantAsync(_tenantA);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*requires an active cross-tenant scope*");
            scope.ServiceProvider.GetRequiredService<ICrossTenantScope>().IsActive.Should().BeFalse();
        }

        (await ReadAsync(sp, request, _tenantA)).Single().Email.Should().Be("a@example.com");
        (await EncryptionHost.QueryAsync(cs, "SELECT shredded_at FROM sk_tenant_encryption_keys")).Single()["shredded_at"].Should().BeNull();
    }

    [Fact]
    public async Task ShredTenant_WithValuesStillUnderARootKey_RefusesByDefault_AndReportsThemWhenAllowed()
    {
        // Finding S3 (a, b, d): values written before tenant data keys stay under the root key. Destroying the tenant
        // key does not erase them, so a shred that reported "erased" while they stay readable would be false.
        var cs = Cs("enc_t_legacy");
        var request = new TestRequestContext();
        await using (var rootOnly = EncryptionHost.Build<CustomerDbContext>(cs, request))
        {
            await CreateDatabaseAsync(rootOnly, cs);
            await AddAsync(rootOnly, request, _tenantA, "legacy@example.com");
        }

        await using var sp = TenantKeyHost(cs, request);
        await AddAsync(sp, request, _tenantA, "new@example.com"); // under the tenant key

        var refuse = () => ShredAsync(sp, _tenantA);
        var refused = await refuse.Should().ThrowAsync<TenantShredIncompleteException>();
        (refused.Which.RootKeyValues, refused.Which.PlaintextValues).Should().Be((2, 0)); // email and iban of the legacy row
        (await EncryptionHost.QueryAsync(cs, "SELECT shredded_at FROM sk_tenant_encryption_keys")).Single()["shredded_at"]
            .Should().BeNull("a refused shred changes nothing");
        (await ReadAsync(sp, request, _tenantA)).Should().HaveCount(2);

        // A plaintext value in an encrypted column is not erased by the shred either.
        await EncryptionHost.ExecuteAsync(cs, "UPDATE customers SET note = 'plaintext note' WHERE id = (SELECT id FROM customers ORDER BY id LIMIT 1)");
        var refusedAgain = await refuse.Should().ThrowAsync<TenantShredIncompleteException>();
        refusedAgain.Which.PlaintextValues.Should().Be(1);
        await EncryptionHost.ExecuteAsync(cs, "UPDATE customers SET note = NULL");

        var result = await ShredAsync(sp, _tenantA, new TenantShredOptions { AllowIncompleteErasure = true });
        result.IsComplete.Should().BeFalse();
        (result.RootKeyValues, result.PlaintextValues).Should().Be((2, 0));

        // (b) The platform refuses the root-key values of a shredded tenant too, in this process and in another.
        var legacyId = (await EncryptionHost.QueryAsync(cs, "SELECT id, email FROM customers"))
            .Where(r => EncryptedPayload.TryParse((string)r["email"]!, out var p) && p.KeyId == "v1")
            .Select(r => (Guid)r["id"]!)
            .Single();
        var read = () => ReadOneAsync(sp, request, _tenantA, legacyId);
        await read.Should().ThrowAsync<TenantKeyShreddedException>();
        await using (var otherProcess = TenantKeyHost(cs, request))
        {
            var readElsewhere = () => ReadOneAsync(otherProcess, request, _tenantA, legacyId);
            await readElsewhere.Should().ThrowAsync<TenantKeyShreddedException>();
        }

        // (d) Honest counts: the root-key values are not reported as shredded, and are never re-indexed.
        var verify = await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.VerifyOnly });
        verify.ShreddedValues.Should().Be(2);                // the new row, under the destroyed tenant key
        verify.ShreddedTenantValuesNotErased.Should().Be(2); // the legacy row, still under v1
        verify.ValuesByKeyId.Should().ContainKey("v1");
        verify.UndecryptableValues.Should().Be(0);

        var recompute = await RunAsync(sp, new EncryptionMaintenanceRequest
        {
            Mode = EncryptionMaintenanceMode.ReEncrypt | EncryptionMaintenanceMode.RecomputeBlindIndexes,
            ExpectedCurrentKeyId = "v1",
        });
        recompute.BlindIndexesRecomputed.Should().Be(0);
        recompute.ValuesReEncrypted.Should().Be(0);
        (await EncryptionHost.QueryAsync(cs, $"SELECT email_blind_index, billing_bank_iban_blind_index FROM customers WHERE tenant_id = '{_tenantA}'"))
            .SelectMany(r => r.Values).Should().OnlyContain(v => v == null, "a shredded tenant's blind indexes are never recomputed");
    }

    [Fact]
    public async Task ShredTenant_InAnotherProcess_StopsThisProcessWriting_ThroughItsCachedKey()
    {
        // Finding S3 (c): the writer caches the unwrapped key; before the fix it kept encrypting new values (with fresh
        // blind indexes) under the destroyed key for up to TenantKeyCacheDuration.
        var cs = Cs("enc_t_other");
        var writerRequest = new TestRequestContext();
        await using var writer = TenantKeyHost(cs, writerRequest);
        await CreateDatabaseAsync(writer, cs);
        await AddAsync(writer, writerRequest, _tenantA, "first@example.com"); // caches the key in the writer

        await using var eraser = TenantKeyHost(cs, new TestRequestContext());
        (await ShredAsync(eraser, _tenantA)).IsComplete.Should().BeTrue();

        // Without an explicit transaction (asynchronous save).
        var plainSave = () => AddAsync(writer, writerRequest, _tenantA, "second@example.com");
        await plainSave.Should().ThrowAsync<TenantKeyShreddedException>();

        // Inside a transaction (the unit of work's shape), synchronous save path.
        writerRequest.TenantId = _tenantA;
        await using (var scope = writer.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Customers.Add(NewCustomer(_tenantA, "third@example.com"));
            var save = () => context.SaveChanges();
            save.Should().Throw<TenantKeyShreddedException>();
        }

        (await EncryptionHost.QueryAsync(cs, "SELECT count(*) AS n FROM customers")).Single()["n"].Should().Be(1L);
    }

    [Fact]
    public async Task ShredTenant_WaitsForAnInFlightWriteTransaction_ThenClearsWhatItWrote()
    {
        var cs = Cs("enc_t_race");
        var writerRequest = new TestRequestContext { TenantId = _tenantA };
        await using var writer = TenantKeyHost(cs, writerRequest);
        await CreateDatabaseAsync(writer, cs);
        await AddAsync(writer, writerRequest, _tenantA, "first@example.com");
        await using var eraser = TenantKeyHost(cs, new TestRequestContext());

        await using var scope = writer.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        var transaction = await context.Database.BeginTransactionAsync();
        context.Customers.Add(NewCustomer(_tenantA, "in-flight@example.com"));
        await context.SaveChangesAsync(); // holds a share lock on the tenant's key row until the transaction ends

        var shred = ShredAsync(eraser, _tenantA);
        (await Task.WhenAny(shred, Task.Delay(TimeSpan.FromSeconds(1)))).Should().NotBeSameAs(shred, "the shred waits for the writer");

        await transaction.CommitAsync();
        await transaction.DisposeAsync();
        var result = await shred;

        result.BlindIndexValuesCleared.Should().Be(4, "both rows' email and iban indexes, including the in-flight row's");
        (await EncryptionHost.QueryAsync(cs, "SELECT email_blind_index, billing_bank_iban_blind_index FROM customers"))
            .SelectMany(r => r.Values).Should().OnlyContain(v => v == null);
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
        var report = await RunAsync(sp, new EncryptionMaintenanceRequest { Mode = EncryptionMaintenanceMode.ReEncrypt, ExpectedCurrentKeyId = "v1" });
        report.ValuesReEncrypted.Should().Be(2);
        report.ValuesByKeyId.Should().ContainKey(EncryptionMaintenanceReport.TenantDataKeysLabel).And.ContainKey("v1");

        EncryptedPayload.TryParse((string)(await EncryptionHost.QueryAsync(cs, "SELECT email FROM customers")).Single()["email"]!, out var customerPayload);
        customerPayload!.KeyId.Should().StartWith("skt:");
        EncryptedPayload.TryParse((string)(await EncryptionHost.QueryAsync(cs, "SELECT body FROM documents")).Single()["body"]!, out var documentPayload);
        documentPayload!.KeyId.Should().Be("v1");
        (await ReadAsync(sp, request, _tenantA)).Single().Email.Should().Be("a@example.com");

        // After the migration the tenant's erasure is complete.
        (await ShredAsync(sp, _tenantA)).IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ShredTenant_WithoutTenantDataKeys_IsRefused()
    {
        var cs = Cs("enc_t_nokeys");
        var request = new TestRequestContext();
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, request);
        await CreateDatabaseAsync(sp, cs);

        var act = () => ShredAsync(sp, _tenantA);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Tenant data keys are not enabled*");
    }
}
