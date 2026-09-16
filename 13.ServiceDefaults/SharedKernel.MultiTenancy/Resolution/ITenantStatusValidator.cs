namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Optional seam letting a consuming service gate tenant resolution on the resolved tenant's
/// active/suspended status.
/// </summary>
/// <remarks>
/// <para>
/// A locally-owned opt-in seam — mirrors <c>05.Application</c>'s <c>IRequestContext</c>/
/// <c>IUnitOfWork</c> bridge pattern, never a direct reference to a specific persistence/cache
/// technology. No default implementation ships; the consuming service bridges this interface to
/// its own tenant directory/cache at its own composition root.
/// </para>
/// <para>
/// Resolved by <see cref="Middleware.TenantResolutionMiddleware"/> as an <b>optional</b> DI
/// service — <see langword="null"/> means "not registered," never "registered but unimplemented."
/// When unregistered (the default), the check is skipped entirely: zero added latency, zero
/// behavior change.
/// </para>
/// <para>
/// A <see langword="false"/> result from <see cref="IsActiveAsync"/> is treated identically to "no
/// strategy resolved a tenant" — the existing <see cref="Guid.Empty"/> fail-closed path, reusing
/// the same <c>MultiTenancyLog.TenantNotResolved</c> log call site rather than a distinct message,
/// so an inactive/suspended tenant is deliberately indistinguishable from an absent one to every
/// downstream consumer (no new tenant-existence information-disclosure surface).
/// </para>
/// <para>
/// <see cref="Resolution.DatabaseTenantResolutionStrategy"/> already provides equivalent
/// protection by construction (its directory lookup IS an existence check) — registering
/// <see cref="ITenantStatusValidator"/> alongside DB-isolation resolution is redundant but
/// harmless, never double-fails-closed in a way that breaks anything.
/// </para>
/// </remarks>
public interface ITenantStatusValidator
{
    /// <summary>
    /// Determines whether <paramref name="tenantId"/> is currently active and permitted to serve
    /// requests.
    /// </summary>
    /// <param name="tenantId">The tenant identifier resolved by an <see cref="ITenantResolutionStrategy"/>.</param>
    /// <param name="ct">The cancellation token for the current request.</param>
    /// <returns>
    /// <see langword="true"/> when the tenant is active; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> IsActiveAsync(Guid tenantId, CancellationToken ct);
}
