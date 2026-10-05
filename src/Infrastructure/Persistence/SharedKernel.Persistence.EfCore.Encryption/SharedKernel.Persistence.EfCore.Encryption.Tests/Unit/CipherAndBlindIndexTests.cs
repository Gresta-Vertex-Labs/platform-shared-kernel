using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Crypto;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

public sealed class CipherAndBlindIndexTests
{
    private static readonly CryptographicKey Root = new("v1", TestKeys.V1);

    [Fact]
    public void EachPurposeHasItsOwnKey_SoAValueNeverDecryptsUnderAnotherPurpose()
    {
        var cipher = new FieldCipher();
        var aad = AssociatedDataBuilder.Build("a.purpose", [1, 2, 3], null);
        var stored = cipher.EncryptWithRootKey(Root, "a.purpose", aad, "secret");
        EncryptedPayload.TryParse(stored, out var payload).Should().BeTrue();

        cipher.TryDecrypt(payload!, "a.purpose", aad, null, _ => Root, _ => null, out var plaintext).Should().Be(DecryptOutcome.Success);
        plaintext.Should().Be("secret");

        // Same associated data, other purpose: fails even before the AAD differs, because the key differs.
        cipher.TryDecrypt(payload!, "b.purpose", aad, null, _ => Root, _ => null, out _).Should().Be(DecryptOutcome.AuthenticationFailed);

        // The payload names the root key id, so rotation tooling reads it without knowing the derivation.
        payload!.KeyId.Should().Be("v1");
    }

    [Fact]
    public void TenantKeyPayload_ForAnotherTenantsRow_IsRejected_AndShreddedKeysAreReported()
    {
        var cipher = new FieldCipher();
        var tenant = Guid.NewGuid();
        var entry = new TenantKeyEntry(tenant, TestKeys.V2.ToArray(), 0);
        var aad = AssociatedDataBuilder.Build("p", [9], tenant);
        EncryptedPayload.TryParse(FieldCipher.EncryptWithTenantKey(entry, "p", aad, "x"), out var payload);

        payload!.KeyId.Should().Be($"skt:{tenant:N}");
        cipher.TryDecrypt(payload, "p", aad, Guid.NewGuid(), _ => null, _ => entry, out _).Should().Be(DecryptOutcome.AuthenticationFailed);
        cipher.TryDecrypt(payload, "p", aad, tenant, _ => null, _ => null, out _).Should().Be(DecryptOutcome.UnknownKey);

        entry.Clear();
        cipher.TryDecrypt(payload, "p", aad, tenant, _ => null, _ => entry, out _).Should().Be(DecryptOutcome.TenantKeyShredded);
    }

    [Fact]
    public void BlindIndex_IsVersioned_Normalized_TenantBound_AndIndependentOfTheEncryptionKey()
    {
        using var host = EncryptionHost.Build<CustomerDbContext>(
            "Host=localhost;Port=1;Database=none;Username=x;Password=y", blindIndexVersion: "v2");
        using var scope = host.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Model;
        var email = EncryptionModelMetadata.For(model).FindMember(model.FindEntityType(typeof(Customer))!, "Email")!;
        var indexer = host.GetRequiredService<BlindIndexer>();
        var tenant = Guid.NewGuid();

        var index = indexer.Compute(email, " Ada@Example.COM ", tenant);
        index.Should().StartWith("v2:").And.HaveLength(3 + 64);
        indexer.Compute(email, "ada@example.com", tenant).Should().Be(index);
        indexer.Compute(email, "ada@example.com", Guid.NewGuid()).Should().NotBe(index);
        indexer.ComputeAllVersions(email, "ada@example.com", tenant).Should().HaveCount(2).And.Contain(index)
            .And.Contain(v => v.StartsWith("v1:"));
    }

    [Fact]
    public void CreateTenantEncryptionKeyTable_EmitsTheTableInTheRequestedSchema()
    {
        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration.CreateTenantEncryptionKeyTable("keys");

        var sql = migration.Operations.OfType<SqlOperation>().Single().Sql;
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS \"keys\".\"sk_tenant_encryption_keys\"")
            .And.Contain("tenant_id uuid NOT NULL PRIMARY KEY");
    }
}
