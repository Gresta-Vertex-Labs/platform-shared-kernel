using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;

namespace SharedKernel.Persistence.EfCore;

/// <summary>
/// <see cref="MigrationBuilder"/> helpers that create and drop the PostgreSQL row-level security (RLS)
/// tenant policy of a table.
/// </summary>
/// <remarks>
/// <para>
/// The policy restricts every statement — <c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, raw
/// SQL included — to rows of the tenant bound in the current transaction:
/// <c>tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid</c>, as both <c>USING</c> and
/// <c>WITH CHECK</c>. With no tenant bound it matches nothing. The predicate is index-friendly; keep an
/// index whose leading column is the tenant column.
/// </para>
/// <para>
/// The policy binds nothing by itself and only restricts roles subject to RLS: not a superuser, not a
/// role with <c>BYPASSRLS</c>, and not the table owner unless RLS is forced (these helpers force it, but
/// an owner can still switch it off). Run migrations as an owner role and the application as a separate,
/// unprivileged role — see the <c>SharedKernel.Persistence.Npgsql</c> README for the role script.
/// </para>
/// </remarks>
public static class RowLevelSecurityMigrationBuilderExtensions
{
    private const string TenantPolicySuffix = "_tenant_isolation";
    private const string CrossTenantPolicySuffix = "_cross_tenant";

    /// <summary>
    /// Enables and forces row-level security on <paramref name="table"/> and creates its tenant policy.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The tenant-scoped table.</param>
    /// <param name="tenantColumn">The tenant column (a <c>uuid</c>). Defaults to <c>tenant_id</c>.</param>
    /// <param name="schema">Optional schema.</param>
    /// <param name="crossTenantRole">
    /// Optional role that may read and write every tenant's rows, through a second, role-specific policy —
    /// the alternative to giving the cross-tenant role <c>BYPASSRLS</c>. The application role must not be a
    /// member of it.
    /// </param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string table,
        string tenantColumn = "tenant_id",
        string? schema = null,
        string? crossTenantRole = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var predicate = TenantSessionSql.PolicyPredicate(PostgresIdentifier.Quote(tenantColumn));

        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"""
            CREATE POLICY {PolicyName(table, TenantPolicySuffix)} ON {qualifiedTable}
            USING ({predicate})
            WITH CHECK ({predicate});
            """);

        if (crossTenantRole is not null)
        {
            migrationBuilder.Sql($"""
                CREATE POLICY {PolicyName(table, CrossTenantPolicySuffix)} ON {qualifiedTable}
                TO {PostgresIdentifier.Quote(crossTenantRole)}
                USING (true)
                WITH CHECK (true);
                """);
        }

        return migrationBuilder;
    }

    /// <summary>
    /// Drops the policies <see cref="EnableTenantRowLevelSecurity"/> created and switches row-level
    /// security off on <paramref name="table"/> (<c>NO FORCE</c> and <c>DISABLE</c>) — the <c>Down</c>
    /// counterpart.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table.</param>
    /// <param name="schema">Optional schema.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder DisableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);

        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName(table, CrossTenantPolicySuffix)} ON {qualifiedTable};");
        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName(table, TenantPolicySuffix)} ON {qualifiedTable};");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;");

        return migrationBuilder;
    }

    /// <summary>
    /// Enables row-level security with the tenant policy on <em>every</em> tenant table of <paramref name="model"/> —
    /// each table of an <c>IHasTenant</c> entity type that holds its tenant column, children of aggregates included.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="model">The model the migration creates, typically the migration's own <c>TargetModel</c>.</param>
    /// <param name="crossTenantRole">Optional role exempt from the policy; see <see cref="EnableTenantRowLevelSecurity"/>.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    /// <remarks>
    /// <code>
    /// protected override void Up(MigrationBuilder migrationBuilder)
    /// {
    ///     // ... CreateTable calls ...
    ///     migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!);
    /// }
    /// </code>
    /// Call it again in a later migration that adds tenant tables, after dropping the policies of the tables it
    /// already covered, or use the per-table <see cref="EnableTenantRowLevelSecurity"/> for the new tables only. A
    /// table of a derived type that does not hold the tenant column itself (table-per-type) is skipped: its rows are
    /// reached through the base table.
    /// </remarks>
    public static MigrationBuilder EnableTenantRowLevelSecurityForModel(
        this MigrationBuilder migrationBuilder,
        IReadOnlyModel model,
        string? crossTenantRole = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(model);

        var tables = TenantTables(model);

        // Fail closed: a call that protects nothing is a mistake (a model without tenant entities, or a migration
        // generated before tenant entity types were annotated), never something to pass over silently.
        if (tables.Count == 0)
        {
            throw new InvalidOperationException(
                "EnableTenantRowLevelSecurityForModel found no tenant tables in the model. Pass the migration's own "
                + "TargetModel of a context whose tenant entities implement IHasTenant; if the migration was generated "
                + "with an earlier SharedKernel.Persistence.EfCore, regenerate it, or protect the tables one by one "
                + "with EnableTenantRowLevelSecurity(table).");
        }

        foreach (var (table, schema, column) in tables)
            migrationBuilder.EnableTenantRowLevelSecurity(table, column, schema, crossTenantRole);

        return migrationBuilder;
    }

    /// <summary>The <c>Down</c> counterpart of <see cref="EnableTenantRowLevelSecurityForModel"/>.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="model">The same model passed to <see cref="EnableTenantRowLevelSecurityForModel"/>.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder DisableTenantRowLevelSecurityForModel(this MigrationBuilder migrationBuilder, IReadOnlyModel model)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentNullException.ThrowIfNull(model);

        foreach (var (table, schema, _) in TenantTables(model))
            migrationBuilder.DisableTenantRowLevelSecurity(table, schema);

        return migrationBuilder;
    }

    /// <summary>The tables of <paramref name="model"/> that hold a tenant column, each once, in model order.</summary>
    internal static IReadOnlyList<(string Table, string? Schema, string TenantColumn)> TenantTables(IReadOnlyModel model)
    {
        var tables = new List<(string Table, string? Schema, string TenantColumn)>();
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !IsTenantEntity(entityType) || entityType.GetTableName() is not { } table)
            {
                continue;
            }

            var schema = entityType.GetSchema();
            var column = entityType.FindProperty(nameof(IHasTenant.TenantId))
                ?.GetColumnName(StoreObjectIdentifier.Table(table, schema));

            if (column is not null && !tables.Exists(t => t.Table == table && t.Schema == schema))
                tables.Add((table, schema, column));
        }

        return tables;
    }

    // A live model has the CLR type. A migration's TargetModel is rebuilt from its Designer file as property bags —
    // no CLR type — so the annotation the domain-column convention stamps is what identifies a tenant entity there.
    private static bool IsTenantEntity(IReadOnlyEntityType entityType)
    {
        if (typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
            return true;

        for (var current = entityType; current is not null; current = current.BaseType)
        {
            if (current.FindAnnotation(PersistenceModelAnnotationNames.Tenant)?.Value is true)
                return true;
        }

        return false;
    }

    /// <summary>The name of the tenant policy <see cref="EnableTenantRowLevelSecurity"/> creates on <paramref name="table"/>.</summary>
    internal static string TenantPolicyName(string table) =>
        Conventions.PostgresIdentifierLengthConvention.Truncate(table + TenantPolicySuffix);

    // PostgreSQL silently truncates an identifier longer than 63 bytes; a long table name would otherwise create
    // a policy whose name the Down migration (and a second table sharing the prefix) could not match. The same
    // deterministic truncate-and-hash the model uses keeps it within the limit and stable across regenerations.
    private static string PolicyName(string table, string suffix) =>
        PostgresIdentifier.Quote(Conventions.PostgresIdentifierLengthConvention.Truncate(table + suffix));
}
