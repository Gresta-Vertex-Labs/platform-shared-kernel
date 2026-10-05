using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Idempotency.Abstractions;

/// <summary>
/// The one encoding of the tenant an idempotency key belongs to, shared by every store so that "no tenant" means the
/// same thing everywhere.
/// </summary>
/// <remarks>
/// A tenant is its <see cref="TenantId"/> in "D" form (36 characters); no tenant is <see cref="NoTenant"/>, which
/// can never equal a GUID string, so an unscoped key can never collide with a tenant's. The tenant is read from the
/// ambient request context at call time, which the service's inbound adapters set (the HTTP request-context
/// middleware, the message consume filter, the job runner). A call made outside any request context, or in a
/// context with no tenant, uses the no-tenant scope.
/// </remarks>
public static class IdempotencyTenantScope
{
    /// <summary>The scope of a key reserved with no tenant.</summary>
    public const string NoTenant = "no-tenant";

    /// <summary>The longest scope value: a tenant id in "D" form.</summary>
    public const int MaxLength = 36;

    /// <summary>Encodes <paramref name="tenantId"/> as a scope value.</summary>
    /// <param name="tenantId">The tenant, or <see langword="null"/> for none.</param>
    /// <returns>The tenant id in "D" form, or <see cref="NoTenant"/>.</returns>
    public static string For(TenantId? tenantId) => tenantId is { } tenant ? tenant.ToString() : NoTenant;

    /// <summary>The scope of the ambient request context.</summary>
    /// <param name="accessor">The ambient request-context accessor.</param>
    /// <returns>The current tenant's scope, or <see cref="NoTenant"/> when there is no context or no tenant.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="accessor"/> is <see langword="null"/>.</exception>
    public static string Current(IRequestContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return For(accessor.Current?.TenantId);
    }
}
