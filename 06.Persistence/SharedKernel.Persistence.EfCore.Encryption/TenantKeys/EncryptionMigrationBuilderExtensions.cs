using Microsoft.EntityFrameworkCore.Migrations;

namespace SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

/// <summary>Migration helpers for field encryption.</summary>
public static class EncryptionMigrationBuilderExtensions
{
    /// <summary>
    /// Creates the table that holds wrapped tenant data keys (<c>sk_tenant_encryption_keys</c>), for
    /// <c>UseTenantDataKeys()</c>. Idempotent.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="schema">The schema; must match <see cref="EncryptionOptions.TenantKeySchema"/>. <see langword="null"/> for the default schema.</param>
    /// <returns>The migration builder.</returns>
    /// <remarks>
    /// The table holds only wrapped keys (useless without the KMS master key) and one tombstone per shredded tenant.
    /// Grant the application role <c>SELECT, INSERT, UPDATE</c> on it; it needs no row-level security policy.
    /// </remarks>
    public static MigrationBuilder CreateTenantEncryptionKeyTable(this MigrationBuilder migrationBuilder, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(TenantKeyStore.CreateTableSql(schema));
        return migrationBuilder;
    }
}
