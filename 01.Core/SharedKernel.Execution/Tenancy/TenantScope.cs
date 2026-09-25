namespace SharedKernel.Execution.Tenancy;

/// <summary>
/// The tenant an operation runs under: one specific tenant, or the global scope that belongs to no tenant.
/// </summary>
/// <remarks>
/// <para>
/// Operations that could leak data across tenants (search, vector queries, workflow dispatch, scheduled
/// jobs) take a <see cref="TenantScope"/> as a required, separate parameter, so a caller always states the
/// scope on purpose. Choosing <see cref="Global"/> is an explicit decision, never a default.
/// </para>
/// <para>
/// <c>default(TenantScope)</c> equals <see cref="Global"/>. That is why the parameter carrying it must
/// never have a default value.
/// </para>
/// </remarks>
public readonly record struct TenantScope
{
    private TenantScope(TenantId? tenant) => Tenant = tenant;

    /// <summary>Gets the scope that belongs to no tenant.</summary>
    public static TenantScope Global => default;

    /// <summary>Gets the tenant, or <see langword="null"/> for the global scope.</summary>
    public TenantId? Tenant { get; }

    /// <summary>Gets a value indicating whether this is the global scope.</summary>
    public bool IsGlobal => Tenant is null;

    /// <summary>Creates the scope of one tenant.</summary>
    /// <param name="tenant">The tenant.</param>
    /// <returns>The tenant's scope.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenant"/> is <c>default(TenantId)</c>.</exception>
    public static TenantScope For(TenantId tenant)
    {
        if (tenant.IsDefault)
            throw new ArgumentException("default(TenantId) names no tenant. Use TenantScope.Global for work that belongs to no tenant.", nameof(tenant));

        return new TenantScope(tenant);
    }

    /// <summary>
    /// Creates the scope for an optional tenant: the tenant's scope when one is given, otherwise
    /// <see cref="Global"/>.
    /// </summary>
    /// <param name="tenant">The tenant, or <see langword="null"/>.</param>
    /// <returns>The scope.</returns>
    public static TenantScope FromNullable(TenantId? tenant) => tenant is { } t ? For(t) : Global;

    /// <summary>Returns the tenant id, or <c>"global"</c> for the global scope.</summary>
    /// <returns>The string form.</returns>
    public override string ToString() => Tenant?.ToString() ?? "global";
}
