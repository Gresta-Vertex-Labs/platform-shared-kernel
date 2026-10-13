using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.Abstractions.Errors;

/// <summary>
/// Builds the <see cref="Error"/> values that several persistence packages must raise identically. The packages
/// that need them (Dapper, EF Core, the audit ledger) cannot see each other's internals, so the single copy is
/// public here.
/// </summary>
public static class PersistenceSharedErrors
{
    /// <summary>
    /// The error for an operation that needs a tenant and finds none: <see cref="PersistenceErrorCodes.Tenancy.TenantRequired"/>,
    /// <see cref="ErrorType.Forbidden"/>, a fixed message that names the remedy and carries no entity or value.
    /// </summary>
    /// <returns>The shared no-tenant error; equal on every call.</returns>
    public static Error TenantRequired()
        => Error.Forbidden(
            PersistenceErrorCodes.Tenancy.TenantRequired,
            "The request has no tenant. Resolve the tenant for this call, or run it inside a cross-tenant scope.");
}
