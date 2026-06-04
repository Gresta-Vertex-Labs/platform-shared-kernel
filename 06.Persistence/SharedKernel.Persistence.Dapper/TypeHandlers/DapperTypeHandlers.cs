using Dapper;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Central registry for platform-wide Dapper type handlers.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Register"/> once at application startup (typically in the DI composition root or
/// via <c>AddSharedKernelDapper()</c>). Subsequent calls are no-ops — the method is idempotent.
/// </para>
/// <para>
/// Service-specific type handlers (e.g., subclasses of
/// <see cref="StronglyTypedIdTypeHandler{TStronglyTypedId, TValue}"/> and
/// <see cref="SmartEnumTypeHandler{TEnum, TValue}"/>) are registered by each consuming service in
/// its own composition root via <see cref="SqlMapper.AddTypeHandler{T}"/>. They are not registered
/// here to avoid coupling the platform to service-specific types.
/// </para>
/// </remarks>
public static class DapperTypeHandlers
{
    private static bool _registered;
    private static readonly object _lock = new();

    /// <summary>
    /// Registers platform-wide Dapper type handlers. Safe to call multiple times — idempotent.
    /// </summary>
    public static void Register()
    {
        if (_registered)
            return;

        lock (_lock)
        {
            if (_registered)
                return;

            // Register platform-wide handlers here.
            // Service-specific StronglyTypedId and SmartEnum handlers are registered by
            // the consuming service, not here.

            // DateTimeOffset handler for PostgreSQL timestamptz compatibility (if Dapper does
            // not handle it natively with Npgsql 10.x, add it here).
            // As of Dapper 2.x with Npgsql, DateTimeOffset is handled correctly without a
            // custom handler — no entry needed.

            _registered = true;
        }
    }
}
