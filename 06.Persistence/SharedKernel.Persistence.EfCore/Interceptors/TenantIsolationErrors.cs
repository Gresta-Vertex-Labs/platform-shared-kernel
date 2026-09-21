using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Builds the <see cref="Error"/> a tenant-isolation write rejection carries. Every case shares one code; the
/// message says what was wrong and what to do.
/// </summary>
internal static class TenantIsolationErrors
{
    /// <summary>The error code of every tenant-isolation rejection.</summary>
    public const string Code = "persistence.tenant_isolation_violation";

    private const string CrossTenantHint =
        " If this is a deliberate cross-tenant operation, run it inside ICrossTenantScope.Enter(reason).";

    /// <summary>The caller has no tenant, so no tenant-scoped row may be written.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    public static Error NoTenant(string entityTypeName) =>
        Error.Forbidden(
            Code,
            $"Writing '{entityTypeName}' was rejected: the caller has no tenant (IRequestContext.TenantId is null), so "
            + "no tenant-scoped data may be written. Resolve the tenant for this request or job." + CrossTenantHint);

    /// <summary>The entity belongs to a tenant other than the caller's.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    public static Error OtherTenant(string entityTypeName) =>
        Error.Forbidden(
            Code,
            $"Writing '{entityTypeName}' was rejected: its TenantId is not the caller's tenant. Create and change only "
            + "the caller's own data (an added entity with no TenantId gets the caller's automatically)." + CrossTenantHint);

    /// <summary>The write would move the entity to another tenant.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    public static Error TenantChanged(string entityTypeName) =>
        Error.Forbidden(
            Code,
            $"Writing '{entityTypeName}' was rejected: its TenantId was changed. Data cannot move between tenants; "
            + "copy it into the other tenant instead." + CrossTenantHint);
}
