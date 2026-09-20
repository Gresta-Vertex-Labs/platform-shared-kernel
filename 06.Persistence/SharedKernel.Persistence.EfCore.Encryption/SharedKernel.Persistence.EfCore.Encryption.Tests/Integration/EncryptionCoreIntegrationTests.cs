using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Extensions;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>
/// Core round-trip, cross-row/cross-tenant isolation, rename-safety, and fail-closed proofs for
/// <see cref="EncryptionInterceptor"/> against real PostgreSQL.
/// </summary>
[Collection("EncryptionPostgres")]
public sealed class EncryptionCoreIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly IClock Clock = new SystemClock();

    public EncryptionCoreIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    [Fact]
    public async Task RoundTrip_EncryptsAtRestAndDecryptsOnRead()
    {
        var connectionString = ConnectionString("sk_enc_roundtrip");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        var id = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, "alice@example.com", "111-22-3333", "hello"));
            await context.SaveChangesAsync();

            // The tracked in-memory instance never observably holds ciphertext after save.
            context.Customers.Local.Single().Email.Should().Be("alice@example.com");
        }

        // Raw column value is genuinely ciphertext, not plaintext.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = "SELECT email FROM customers WHERE id = @id";
            command.Parameters.AddWithValue("id", id.Value);
            var stored = (string)(await command.ExecuteScalarAsync())!;
            stored.Should().NotBe("alice@example.com");
            EncryptedPayload.TryParse(stored, out _).Should().BeTrue();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            var loaded = await context.Customers.SingleAsync(x => x.Id == id);
            loaded.Email.Should().Be("alice@example.com");
            loaded.Ssn.Should().Be("111-22-3333");
            loaded.Note.Should().Be("hello");
        }
    }

    [Fact]
    public async Task NullableEncryptedProperty_RoundTripsNull()
    {
        var connectionString = ConnectionString("sk_enc_nullable");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        var id = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, "bob@example.com", "222-33-4444", note: null));
            await context.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            var loaded = await context.Customers.SingleAsync(x => x.Id == id);
            loaded.Note.Should().BeNull();
        }
    }

    [Fact]
    public async Task CiphertextSwappedBetweenRows_FailsToDecrypt()
    {
        var connectionString = ConnectionString("sk_enc_row_swap");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        var idA = EncCustomerId.New();
        var idB = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(idA, tenantId, Clock, "a@example.com", "111-11-1111"));
            context.Customers.Add(new EncCustomer(idB, tenantId, Clock, "b@example.com", "222-22-2222"));
            await context.SaveChangesAsync();
        }

        // Copy row A's ciphertext into row B's column directly — no EF Core, no interceptors.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = """
                UPDATE customers SET email = (SELECT email FROM customers WHERE id = @a)
                WHERE id = @b
                """;
            command.Parameters.AddWithValue("a", idA.Value);
            command.Parameters.AddWithValue("b", idB.Value);
            await command.ExecuteNonQueryAsync();
        }

        await using var readScope = sp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var act = () => readContext.Customers.SingleAsync(x => x.Id == idB);
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task CiphertextSwappedBetweenTenants_FailsToDecrypt()
    {
        var connectionString = ConnectionString("sk_enc_tenant_swap");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantA);
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: actor);
        await EnsureCreatedAsync(sp);

        var idA = EncCustomerId.New();
        var idB = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(idA, tenantA, Clock, "a@example.com", "111-11-1111"));
            await context.SaveChangesAsync();
        }

        actor.TenantId = tenantB;
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(idB, tenantB, Clock, "b@example.com", "222-22-2222"));
            await context.SaveChangesAsync();
        }

        // Ssn is per-tenant-keyed AND bound to tenant id in AAD — copy A's Ssn ciphertext to B, same tenant B row.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = """
                UPDATE customers SET ssn = (SELECT ssn FROM customers WHERE id = @a)
                WHERE id = @b
                """;
            command.Parameters.AddWithValue("a", idA.Value);
            command.Parameters.AddWithValue("b", idB.Value);
            await command.ExecuteNonQueryAsync();
        }

        await using var readScope = sp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var act = () => readContext.Customers.SingleAsync(x => x.Id == idB);
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task CiphertextSwappedBetweenTenants_PerTenantKeyFalse_StillFailsToDecrypt()
    {
        // Email opts OUT of perTenantKey (the same root key material encrypts every tenant's Email), but AAD
        // still binds the tenant id unconditionally for every IHasTenant entity — so a cross-tenant swap of a
        // NON-per-tenant-keyed property must fail exactly like the per-tenant-keyed Ssn case above.
        var connectionString = ConnectionString("sk_enc_tenant_swap_no_pertenantkey");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantA);
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: actor);
        await EnsureCreatedAsync(sp);

        var idA = EncCustomerId.New();
        var idB = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(idA, tenantA, Clock, "a@example.com", "111-11-1111"));
            await context.SaveChangesAsync();
        }

        actor.TenantId = tenantB;
        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(idB, tenantB, Clock, "b@example.com", "222-22-2222"));
            await context.SaveChangesAsync();
        }

        // Copy tenant A's Email ciphertext into tenant B's row — same root key (no perTenantKey derivation
        // involved), only the AAD's tenant component differs.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using var command = raw.CreateCommand();
            command.CommandText = """
                UPDATE customers SET email = (SELECT email FROM customers WHERE id = @a)
                WHERE id = @b
                """;
            command.Parameters.AddWithValue("a", idA.Value);
            command.Parameters.AddWithValue("b", idB.Value);
            await command.ExecuteNonQueryAsync();
        }

        await using var readScope = sp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var act = () => readContext.Customers.SingleAsync(x => x.Id == idB);
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task RenamingTableAndColumn_StillDecrypts()
    {
        var connectionString = ConnectionString("sk_enc_rename");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        var id = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, "carol@example.com", "333-44-5555"));
            await context.SaveChangesAsync();
        }

        // Rename the physical table and column — AAD binds purpose+pk+tenant only, never physical names.
        await using (var raw = new NpgsqlConnection(connectionString))
        {
            await raw.OpenAsync();
            await using (var rename1 = raw.CreateCommand())
            {
                rename1.CommandText = "ALTER TABLE customers RENAME TO customers_renamed";
                await rename1.ExecuteNonQueryAsync();
            }

            await using (var rename2 = raw.CreateCommand())
            {
                rename2.CommandText = "ALTER TABLE customers_renamed RENAME COLUMN email TO email_renamed";
                await rename2.ExecuteNonQueryAsync();
            }

            await using (var rename3 = raw.CreateCommand())
            {
                rename3.CommandText = "ALTER TABLE customers_renamed RENAME TO customers";
                await rename3.ExecuteNonQueryAsync();
            }

            await using (var rename4 = raw.CreateCommand())
            {
                rename4.CommandText = "ALTER TABLE customers RENAME COLUMN email_renamed TO email";
                await rename4.ExecuteNonQueryAsync();
            }
        }

        await using var readScope = sp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var loaded = await readContext.Customers.SingleAsync(x => x.Id == id);
        loaded.Email.Should().Be("carol@example.com");
    }

    [Fact]
    public async Task UnknownKeyId_ThrowsEncryptionKeyNotFoundException()
    {
        var connectionString = ConnectionString("sk_enc_unknown_key");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));
        await EnsureCreatedAsync(sp);

        var id = EncCustomerId.New();

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, "dave@example.com", "444-55-6666"));
            await context.SaveChangesAsync();
        }

        // A host whose key ring never included "v1" (the actual key used above) — only "v2".
        await using var strangerSp = EncryptionTestHost.Build(
            connectionString,
            currentKeyId: "v2",
            actorContext: new FakeAuditActorContext(tenantId: tenantId),
            configureServices: services =>
            {
                var solitaryKey = new StaticEncryptionKeyProviderOnly("v2");
                services.AddSingleton<IEncryptionKeyProvider>(solitaryKey);
                services.AddSingleton<ISynchronousEncryptionKeyProvider>(solitaryKey);
            });

        await using var readScope = strangerSp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var act = () => readContext.Customers.SingleAsync(x => x.Id == id);
        await act.Should().ThrowAsync<EncryptionKeyNotFoundException>();
    }

    [Fact]
    public async Task Pooling_TwoConcurrentTenants_SharingOnePool_EncryptedAndBlindIndexedRoundTripsCorrectly()
    {
        // Deliberately NOT EncryptionTestHost.Build: actor/tenant identity must be SCOPED here (one instance
        // per concurrent scope, mutated to that scope's own tenant before the pooled context is leased) —
        // EncryptionTestHost.Build registers a single shared (Singleton) FakeAuditActorContext, which is right
        // for every other test in this file but wrong for a genuine concurrent-pooled-tenants proof.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddScoped<FakeAuditActorContext>();
        services.AddScoped<SharedKernel.Persistence.Abstractions.Context.ICurrentActorContext>(sp => sp.GetRequiredService<FakeAuditActorContext>());
        services.AddScoped<SharedKernel.Persistence.Abstractions.Context.ICurrentTenantContext>(sp => sp.GetRequiredService<FakeAuditActorContext>());

        var keyProvider = new StaticEncryptionKeyProvider("v1", [new CryptographicKey("v1", EncryptionTestHost.KeyV1), new CryptographicKey("v2", EncryptionTestHost.KeyV2)]);
        services.AddSingleton(keyProvider);
        services.AddSingleton<IEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(sp => sp.GetRequiredService<StaticEncryptionKeyProvider>());

        services
            .AddSharedKernelEfCore<EncryptionTestDbContext>(options => options
                .UsePostgreSQL(ConnectionString("sk_enc_pooled_tenants"))
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                        .WithMultiTenancy()
                            .WithDbContextPooling(poolSize: 2)
                                .WithEncryption()
                                    .Build();

        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using (var setupScope = sp.CreateScope())
            await setupScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>().Database.EnsureCreatedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var idA = EncCustomerId.New();
        var idB = EncCustomerId.New();

        // Genuinely concurrent: both tasks run their whole scope lifetime — resolve a pooled context, write,
        // read back, blind-index lookup, assert — in parallel against a 2-slot pool, so the SAME pooled
        // EncryptionInterceptor singleton is genuinely exercised by two different tenants' leases at once.
        async Task RunForTenant(Guid tenantId, EncCustomerId id, string email, string ssn)
        {
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<FakeAuditActorContext>().TenantId = tenantId;

            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            context.Customers.Add(new EncCustomer(id, tenantId, Clock, email, ssn));
            await context.SaveChangesAsync();

            await Task.Delay(25); // widen the overlap window so both tasks' write+read windows genuinely overlap.

            var reloaded = await context.Customers.SingleAsync(x => x.Id == id);
            reloaded.Email.Should().Be(email);
            reloaded.Ssn.Should().Be(ssn);

            var blindIndexService = scope.ServiceProvider.GetRequiredService<IBlindIndexService>();
            var found = await context.Customers
                .WhereBlindIndexEquals(blindIndexService, x => x.Email, "customer.email", email, tenantId: tenantId)
                    .ToListAsync();
            found.Should().ContainSingle(x => x.Id == id);
        }

        await Task.WhenAll(
            RunForTenant(tenantA, idA, "pool-a@example.com", "111-11-1111"),
            RunForTenant(tenantB, idB, "pool-b@example.com", "222-22-2222"));

        // Cross-check from a third, fresh scope: neither tenant's row leaked into the other's.
        using var verifyScopeA = sp.CreateScope();
        verifyScopeA.ServiceProvider.GetRequiredService<FakeAuditActorContext>().TenantId = tenantA;
        var visibleToA = await verifyScopeA.ServiceProvider.GetRequiredService<EncryptionTestDbContext>().Customers.ToListAsync();
        visibleToA.Should().ContainSingle(x => x.Id == idA);

        using var verifyScopeB = sp.CreateScope();
        verifyScopeB.ServiceProvider.GetRequiredService<FakeAuditActorContext>().TenantId = tenantB;
        var visibleToB = await verifyScopeB.ServiceProvider.GetRequiredService<EncryptionTestDbContext>().Customers.ToListAsync();
        visibleToB.Should().ContainSingle(x => x.Id == idB);
    }

    [Fact]
    public async Task EncryptAnnotationWithoutWithEncryption_ThrowsAtModelBuild()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var actor = new FakeAuditActorContext();
        services.AddSingleton(actor);
        services.AddSingleton<SharedKernel.Persistence.Abstractions.Context.ICurrentActorContext>(actor);
        services.AddSingleton<SharedKernel.Persistence.Abstractions.Context.ICurrentTenantContext>(actor);

        services.AddSharedKernelEfCore<EncryptionTestDbContext>(options => options.UsePostgreSQL(ConnectionString("sk_enc_not_registered")).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithMultiTenancy()
                .Build(); //.WithEncryption() never called.

        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();

        var act = () => context.Model.GetEntityTypes().ToList();
        act.Should().Throw<InvalidOperationException>().WithMessage("*WithEncryption*");
    }

    [Fact]
    public async Task StoreGeneratedPrimaryKey_ThrowsAtSave()
    {
        var connectionString = ConnectionString("sk_enc_store_generated_key");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();

        var entry = context.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, "eve@example.com", "555-66-7777"));

        // Simulate a store-generated key by marking it temporary, exactly as EF Core would for an identity column.
        entry.Property(nameof(EncCustomer.Id)).IsTemporary = true;

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*store-generated*");
    }

    private static async Task EnsureCreatedAsync(IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();
    }
}
