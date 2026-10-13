using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Exceptions;

/// <summary>
/// Builds the <see cref="Error"/> values the EF Core package raises. The codes are declared once in
/// <see cref="PersistenceErrorCodes"/>; messages are fixed sentences that name the remedy and carry no row payload,
/// tenant id or actor. "No tenant" is not here: every persistence package raises the one
/// <see cref="PersistenceSharedErrors.TenantRequired"/> error.
/// </summary>
internal static class EfCoreErrors
{
    private const string CrossTenantHint =
        " If this is a deliberate cross-tenant operation, run it inside ICrossTenantScope.Enter(reason).";

    /// <summary>A save met a stale, foreign or unknown-key version, or a row of another tenant.</summary>
    /// <param name="entityTypeName">The CLR type name of the entity. Never the row payload.</param>
    public static Error ConcurrencyConflict(string entityTypeName) =>
        Error.Conflict(
            PersistenceErrorCodes.Concurrency.Conflict,
            $"'{entityTypeName}' was changed or deleted by someone else. Reload it and retry.");

    /// <summary>The entity belongs to a tenant other than the caller's.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    public static Error OtherTenant(string entityTypeName) =>
        Error.Forbidden(
            PersistenceErrorCodes.Tenancy.TenantIsolationViolation,
            $"Writing '{entityTypeName}' was rejected: its TenantId is not the caller's tenant. Create and change only "
            + "the caller's own data (an added entity with no TenantId gets the caller's automatically)." + CrossTenantHint);

    /// <summary>The write would move the entity to another tenant.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    public static Error TenantChanged(string entityTypeName) =>
        Error.Forbidden(
            PersistenceErrorCodes.Tenancy.TenantIsolationViolation,
            $"Writing '{entityTypeName}' was rejected: its TenantId was changed. Data must not move between tenants; "
            + "copy it into the other tenant instead." + CrossTenantHint);

    /// <summary>A specification has a shape that has no meaning for one server-side <c>ExecuteUpdate</c> or <c>ExecuteDelete</c> statement.</summary>
    /// <param name="reason">A fixed sentence naming the unsupported shape and the remedy.</param>
    public static Error BulkSpecificationNotSupported(string reason) =>
        Error.Validation(
            PersistenceErrorCodes.BulkMutation.SpecificationNotSupported,
            $"The specification is not supported by bulk mutation operations: {reason}");
}
