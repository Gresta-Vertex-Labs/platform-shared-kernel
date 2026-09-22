namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// Row-level security settings of <see cref="NpgsqlPersistenceOptions"/>
/// (<c>RowLevelSecurity</c> in the database's settings section, <c>SharedKernel:Persistence:{connection name}:RowLevelSecurity</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Enabled"/> is normally switched on in code by <c>UseMultiTenancy(rowLevelSecurity: true)</c> on the
/// <c>AddSharedKernelPostgres</c> builder; a Dapper-only service sets it in configuration. When on:
/// </para>
/// <list type="bullet">
/// <item><description>the connection strings may not use <c>Multiplexing</c> or <c>No Reset On Close</c>;</description></item>
/// <item><description>Dapper sessions bind the tenant to their transaction;</description></item>
/// <item><description>a startup check verifies the application role cannot bypass or widen RLS (<see cref="PrivilegeCheck"/>);</description></item>
/// <item><description>work inside an active <c>ICrossTenantScope</c> runs on <see cref="CrossTenantConnectionString"/>.</description></item>
/// </list>
/// </remarks>
public sealed class NpgsqlRowLevelSecurityOptions
{
    /// <summary>Whether tenant row-level security is in use. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Connection string of the dedicated cross-tenant role (a role with <c>BYPASSRLS</c>, or one named by a
    /// role-specific policy), with its own credentials. Optional; without it, cross-tenant work under
    /// row-level security is refused.
    /// </summary>
    /// <remarks>
    /// The application role must NOT be a member of this role: membership would let any SQL the
    /// application runs switch to it with <c>SET ROLE</c>.
    /// </remarks>
    public string? CrossTenantConnectionString { get; set; }

    /// <summary>
    /// What happens when the startup check finds that the application role is a superuser, has
    /// <c>BYPASSRLS</c>, owns a table with row-level security, or is subject to a permissive policy that does not read the
    /// tenant (typically: it is a member of the cross-tenant role). Defaults to
    /// <see cref="RowLevelSecurityPrivilegeCheck.Fail"/>.
    /// </summary>
    public RowLevelSecurityPrivilegeCheck PrivilegeCheck { get; set; } = RowLevelSecurityPrivilegeCheck.Fail;
}

/// <summary>Outcome of a failed row-level security privilege check at startup.</summary>
public enum RowLevelSecurityPrivilegeCheck
{
    /// <summary>The host fails to start.</summary>
    Fail = 0,

    /// <summary>A warning is logged and the host starts.</summary>
    Warn = 1,

    /// <summary>No check is made.</summary>
    Disabled = 2,
}
