using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

[Collection("EncryptionPostgres")]
public sealed class SaveAndReadTests(PostgreSqlContainerFixture fixture)
{
    private readonly TenantId _tenant = new TenantId(Guid.NewGuid());

    private string Cs(string db) => EncryptionHost.Database(fixture.ConnectionString, db);

    private static Customer NewCustomer(TenantId tenant) => new()
    {
        TenantId = tenant,
        Name = "Ada",
        Email = "  Ada@Example.com ",
        Note = "vip",
        Billing = new Address { City = "Ankara", Bank = new BankAccount { Iban = "tr33 0006 1005", BankName = "Z" } },
    };

    [Fact]
    public async Task RoundTrip_StoresCiphertext_IncludingNestedComplexProperty_AndReadsPlaintext()
    {
        var cs = Cs("enc_roundtrip");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        var customer = NewCustomer(_tenant);
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            context.Customers.Add(customer);
            await context.SaveChangesAsync();
        }

        var row = (await EncryptionHost.QueryAsync(cs, "SELECT * FROM customers")).Single();
        row["email"].Should().NotBeNull().And.NotBe(customer.Email);
        ((string)row["email"]!).Should().NotContain("Ada");
        // Finding A9: a property two complex levels deep was stored as plaintext.
        ((string)row["billing_bank_iban"]!).Should().NotContain("0006");
        ((string)row["billing_bank_iban_blind_index"]!).Should().StartWith("v1:");
        row["billing_bank_bank_name"].Should().Be("Z");

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var loaded = await context.Customers.AsNoTracking().SingleAsync();
            loaded.Email.Should().Be(customer.Email);
            loaded.Note.Should().Be("vip");
            loaded.Billing.Bank.Iban.Should().Be("tr33 0006 1005");
        }
    }

    [Fact]
    public async Task NullValue_IsStoredAsNull_AndCanBeFilteredByNullness()
    {
        var cs = Cs("enc_nulls");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        var customer = NewCustomer(_tenant);
        customer.Note = null;
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        (await context.Customers.CountAsync(x => x.Note == null)).Should().Be(1);
        (await context.Customers.CountAsync(x => x.Note != null)).Should().Be(0);
    }

    [Fact]
    public async Task AfterSave_EntryIsUnchanged_OriginalIsPlaintext_AndASecondSaveWritesNothing()
    {
        var cs = Cs("enc_poststate");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        var customer = NewCustomer(_tenant);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Finding A11: the entry stayed Modified with ciphertext as its original value.
        var entry = context.Entry(customer);
        entry.State.Should().Be(EntityState.Unchanged);
        entry.Property(x => x.Email).OriginalValue.Should().Be(customer.Email);
        entry.Property(x => x.Email).IsModified.Should().BeFalse();
        context.ChangeTracker.HasChanges().Should().BeFalse();
        (await context.SaveChangesAsync()).Should().Be(0);
        customer.Email.Should().Be("  Ada@Example.com ");
    }

    [Fact]
    public async Task UnrelatedChange_DoesNotReEncryptOrRewriteEncryptedColumns()
    {
        var cs = Cs("enc_unrelated");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            context.Customers.Add(NewCustomer(_tenant));
            await context.SaveChangesAsync();
        }

        var before = (await EncryptionHost.QueryAsync(cs, "SELECT email, note, billing_bank_iban FROM customers")).Single();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var loaded = await context.Customers.SingleAsync();
            context.Entry(loaded).State.Should().Be(EntityState.Unchanged);
            loaded.Name = "Grace";
            (await context.SaveChangesAsync()).Should().Be(1);
            context.Entry(loaded).State.Should().Be(EntityState.Unchanged);
            (await context.SaveChangesAsync()).Should().Be(0);

            loaded.Email = "grace@example.com";
            await context.SaveChangesAsync();
        }

        var after = (await EncryptionHost.QueryAsync(cs, "SELECT email, note, billing_bank_iban FROM customers")).Single();
        after["note"].Should().Be(before["note"]);
        after["billing_bank_iban"].Should().Be(before["billing_bank_iban"]);
        after["email"].Should().NotBe(before["email"]);
    }

    [Fact]
    public async Task SaveInterruptedAfterEncryption_IsRestored_SoTheNextSaveNeverEncryptsTwice()
    {
        var cs = Cs("enc_stale");
        var thrower = new ThrowOnceAfterEncryption();
        await using var sp = EncryptionHost.Build<CustomerDbContext>(
            cs,
            new TestRequestContext { TenantId = _tenant },
            configureServices: s => s.AddSingleton<IPersistenceOptionsExtension>(thrower));
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
        var customer = NewCustomer(_tenant);
        context.Customers.Add(customer);

        thrower.Armed = true;
        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*interrupted*");

        await context.SaveChangesAsync();
        customer.Email.Should().Be("  Ada@Example.com ");

        await using var readScope = sp.CreateAsyncScope();
        var loaded = await readScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().SingleAsync();
        loaded.Email.Should().Be("  Ada@Example.com ");
    }

    [Fact]
    public async Task CiphertextCopiedToAnotherRowOrTenant_FailsToDecrypt()
    {
        var cs = Cs("enc_swap");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        Guid first;
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            var a = NewCustomer(_tenant);
            var b = NewCustomer(_tenant);
            b.Email = "other@example.com";
            context.Customers.AddRange(a, b);
            await context.SaveChangesAsync();
            first = a.Id;
        }

        await EncryptionHost.ExecuteAsync(cs, $"UPDATE customers SET email = (SELECT email FROM customers WHERE id <> '{first}') WHERE id = '{first}'");

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var act = () => context.Customers.AsNoTracking().SingleAsync(x => x.Id == first);
            await act.Should().ThrowAsync<CryptographicException>().WithMessage("*Customer.Email*");
        }
    }

    [Fact]
    public async Task StoredPlaintext_FailsClosed_WithGuidanceToMigrate()
    {
        var cs = Cs("enc_plain");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            context.Customers.Add(NewCustomer(_tenant));
            await context.SaveChangesAsync();
        }

        await EncryptionHost.ExecuteAsync(cs, "UPDATE customers SET note = 'planted'");
        await using var readScope = sp.CreateAsyncScope();
        var act = () => readScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().ToListAsync();
        await act.Should().ThrowAsync<CryptographicException>().WithMessage("*EncryptPlaintext*");
    }

    [Fact]
    public async Task UnknownKeyId_ThrowsEncryptionKeyNotFound()
    {
        var cs = Cs("enc_unknownkey");
        await using (var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant }))
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);
            context.Customers.Add(NewCustomer(_tenant));
            await context.SaveChangesAsync();
        }

        await using var other = EncryptionHost.Build<CustomerDbContext>(
            cs, new TestRequestContext { TenantId = _tenant },
            configure: k => k.UseKeyProvider(_ => new StaticEncryptionKeyProvider("v9", [new CryptographicKey("v9", TestKeys.V2)])));
        await using var readScope = other.CreateAsyncScope();
        var act = () => readScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Customers.AsNoTracking().ToListAsync();
        (await act.Should().ThrowAsync<EncryptionKeyNotFoundException>()).Which.KeyId.Should().Be("v1");
    }

    [Fact]
    public async Task DeclaredPlaintextLength_IsWidenedForCiphertext_SoAMaximumLengthValueFits()
    {
        var cs = Cs("enc_sizing");
        await using var sp = EncryptionHost.Build<CustomerDbContext>(cs, new TestRequestContext { TenantId = _tenant });
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<CustomerDbContext>(scope);

        // Finding A14: '.HasMaxLength(20).Encrypt(...)' made every insert fail (the ciphertext is ~3x longer).
        context.Model.FindEntityType(typeof(Document))!.FindProperty(nameof(Document.Body))!.GetMaxLength()
            .Should().BeGreaterThan(20 * 4);
        context.Documents.Add(new Document { Id = 1, Body = "ğğğğğğğğğğğğğğğğğğğğ", Title = "t" });
        await context.SaveChangesAsync();
        (await context.Documents.AsNoTracking().SingleAsync()).Body.Should().Be("ğğğğğğğğğğğğğğğğğğğğ");
    }

    [Fact]
    public async Task StoreGeneratedKey_IsRejectedAtSave()
    {
        var cs = Cs("enc_generated");
        await using var sp = EncryptionHost.Build<StoreGeneratedKeyDbContext>(cs);
        await using var scope = sp.CreateAsyncScope();
        var context = await EncryptionHost.CreateDatabaseAsync<StoreGeneratedKeyDbContext>(scope);
        context.Documents.Add(new Document { Body = "x" });
        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*store-generated*");
    }

    private sealed class ThrowOnceAfterEncryption : IPersistenceOptionsExtension, ISaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public void Apply(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(this);

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                throw new InvalidOperationException("Save interrupted by a later interceptor.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
