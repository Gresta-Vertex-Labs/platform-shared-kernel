using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Builds the <see cref="Error"/> a tenant-isolation write rejection carries. The other-tenant and tenant-changed
/// cases share one code; the message says what was wrong and what to do. "No tenant" is not here: every
/// persistence package raises the one <see cref="PersistenceSharedErrors.TenantRequired"/> error.
/// </summary>
internal static class TenantIsolationErrors
{
    /// <summary>The error code of every tenant-isolation rejection.</summary>
    public const string Code = PersistenceErrorCodes.TenantIsolationViolation;

    private const string CrossTenantHint =
        " If this is a deliberate cross-tenant operation, run it inside ICrossTenantScope.Enter(reason).";

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
