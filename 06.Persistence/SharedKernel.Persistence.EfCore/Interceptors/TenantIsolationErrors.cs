using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Builds the one <see cref="Error"/> a tenant-isolation write rejection carries, whichever enforcement point
/// raised it: the save interceptor (in memory, before the save) or the concurrency translator (after the
/// database proved the targeted row belongs to another tenant).
/// </summary>
internal static class TenantIsolationErrors
{
    /// <summary>The error code of every tenant-isolation rejection.</summary>
    public const string Code = "persistence.tenant_isolation_violation";

    /// <summary>Builds the error for <paramref name="entityTypeName"/>.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    /// <param name="reason">A short clause completing "was {reason} the current tenant".</param>
    public static Error Build(string entityTypeName, string reason) =>
        Error.Forbidden(
            Code,
            $"A write was rejected: '{entityTypeName}' was {reason} the current tenant. " +
            "Enter an ICrossTenantScope explicitly if this write is a deliberate cross-tenant operation.");
}
