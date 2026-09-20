using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Integration;

/// <summary>Blind-index equality lookup, and the without-blind-index equality guard, against real PostgreSQL.</summary>
[Collection("EncryptionPostgres")]
public sealed class EncryptionBlindIndexIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly IClock Clock = new SystemClock();

    public EncryptionBlindIndexIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = database }.ConnectionString;

    [Fact]
    public async Task WhereBlindIndexEquals_FindsMatchingRow_CaseAndWhitespaceNormalized()
    {
        var connectionString = ConnectionString("sk_enc_blind_index");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, " Frank@Example.com ".Trim(), "111-11-1111"));
            await context.SaveChangesAsync();
        }

        await using var readScope = sp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var blindIndexService = readScope.ServiceProvider.GetRequiredService<IBlindIndexService>();

        var found = await readContext.Customers
            .WhereBlindIndexEquals(
                blindIndexService,
                x => x.Email,
                "customer.email",
                "FRANK@example.com",
                normalize: static s => s.Trim().ToLowerInvariant(),
                tenantId: tenantId) // the blind index is tenant-bound for every IHasTenant entity, regardless of perTenantKey.
                    .ToListAsync();

        found.Should().ContainSingle();
        found[0].Email.Should().Be("Frank@Example.com"); // blind index normalizes for lookup only — the stored plaintext keeps its original casing.
    }

    [Fact]
    public async Task WhereBlindIndexEquals_DifferentTenant_FindsNothing()
    {
        var connectionString = ConnectionString("sk_enc_blind_index_tenant");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var actor = new FakeAuditActorContext(tenantId: tenantA);
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: actor);

        await using (var scope = sp.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantA, Clock, "grace@example.com", "222-22-2222"));
            await context.SaveChangesAsync();
        }

        actor.TenantId = tenantB;
        await using var readScope = sp.CreateAsyncScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        var blindIndexService = readScope.ServiceProvider.GetRequiredService<IBlindIndexService>();

        // Same email, but the tenant global query filter alone already scopes this to tenant B, where no
        // matching row exists — AND, even without that filter, the blind index itself is tenant-bound (tenantB
        // here), so it would not equal the one stored for tenant A's row either.
        var found = await readContext.Customers
            .WhereBlindIndexEquals(blindIndexService, x => x.Email, "customer.email", "grace@example.com", normalize: static s => s.Trim().ToLowerInvariant(), tenantId: tenantB)
                .ToListAsync();

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task EqualityWithoutBlindIndex_ThrowsInsteadOfSilentlyReturningNothing()
    {
        var connectionString = ConnectionString("sk_enc_equality_guard");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, "henry@example.com", "333-33-3333"));
        await context.SaveChangesAsync();

        // Ssn has no blind index — a direct equality filter must fail loudly, not silently return nothing.
        var query = context.Customers.Where(x => x.Ssn == "333-33-3333");
        var sql = query.ToQueryString();
        var act = () => query.ToListAsync();
        await act.Should().ThrowAsync<InvalidOperationException>(because: $"generated SQL was: {sql}").WithMessage("*WithBlindIndex*");
    }

    [Fact]
    public async Task EqualityOnBlindIndexShadowColumnItself_DoesNotFalsePositive()
    {
        var connectionString = ConnectionString("sk_enc_guard_blindindex_column");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, "ivan@example.com", "444-44-4444"));
        await context.SaveChangesAsync();

        // Email has a blind index, so it is NOT in the equality guard's guarded-column list at all — an equality
        // filter directly against its own shadow blind-index column (exactly the column
        // WhereBlindIndexEquals compares against internally) must never be mistaken for an unguarded plaintext
        // comparison and must never throw.
        var act = () => context.Customers
            .Where(x => EF.Property<string>(x, "EmailBlindIndex") == "0000000000000000000000000000000000000000000000000000000000000000")
                .ToListAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EqualityOnUnrelatedColumnSharingNameSuffix_DoesNotFalsePositive()
    {
        var connectionString = ConnectionString("sk_enc_guard_suffix_column");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, "julia@example.com", "555-55-5555"));
        await context.SaveChangesAsync();

        // BillingAddress.Line1 (column "billing_line1") is encrypted with no blind index, so it IS in the guard's
        // list. ShippingAddress.Line1 (column "shipping_line1") is a completely unrelated, never-encrypted
        // property whose column name merely shares the suffix "line1" with the guarded one — the guard's
        // column-name matching must be exact, not a substring/suffix match, so filtering on the unrelated column
        // must never throw.
        var act = () => context.Customers
            .Where(x => x.ShippingAddress.Line1 == "1 Ship St")
                .ToListAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisableTag_SuppressesGuard_ForOneQuery()
    {
        var connectionString = ConnectionString("sk_enc_guard_disable_tag");
        var tenantId = Guid.NewGuid();
        await using var sp = EncryptionTestHost.Build(connectionString, actorContext: new FakeAuditActorContext(tenantId: tenantId));

        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptionTestDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Customers.Add(new EncCustomer(EncCustomerId.New(), tenantId, Clock, "karl@example.com", "666-66-6666"));
        await context.SaveChangesAsync();

        // Untagged: the guard fires — Ssn has no blind index, confirming the baseline behavior this test's
        // tagged half is opting out of.
        var untagged = () => context.Customers.Where(x => x.Ssn == "666-66-6666").ToListAsync();
        await untagged.Should().ThrowAsync<InvalidOperationException>();

        // Tagged with the escape hatch: the identical filter is let through. It still finds nothing (the column
        // holds ciphertext, so an un-converted plaintext comparison can never truly match) — the tag disables the
        // GUARD, not the fact that the query is meaningless — but it must no longer throw.
        var tagged = await context.Customers
            .TagWith(EncryptedColumnEqualityGuardInterceptor.DisableTagText)
                .Where(x => x.Ssn == "666-66-6666")
                    .ToListAsync();

        tagged.Should().BeEmpty();
    }
}
