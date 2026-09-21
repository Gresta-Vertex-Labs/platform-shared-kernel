namespace SharedKernel.Persistence.Testing;

/// <summary>
/// The canonical roles of the SharedKernel persistence packages (the role script in the
/// <c>SharedKernel.Persistence.Npgsql</c> README), as <see cref="PostgresTestServer"/> creates them. Each role's
/// password is its name — test servers only.
/// </summary>
public static class PostgresTestRoles
{
    /// <summary>Owns every table and runs migrations (<c>MigrationConnectionString</c>).</summary>
    public const string Migrator = "app_migrator";

    /// <summary>The application at runtime (<c>ConnectionStrings:{name}</c>): no superuser, no BYPASSRLS, owns nothing.</summary>
    public const string Runtime = "app_runtime";

    /// <summary>Cross-tenant work under row-level security (<c>RowLevelSecurity:CrossTenantConnectionString</c>): BYPASSRLS.</summary>
    public const string CrossTenant = "app_cross_tenant";

    /// <summary>The audit sealer's own data source: reads records, appends links and checkpoints.</summary>
    public const string AuditSealer = "app_audit_sealer";
}
