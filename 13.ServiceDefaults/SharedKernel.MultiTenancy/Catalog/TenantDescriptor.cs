namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// Read-only tenant metadata returned by <see cref="ITenantCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>READ-ONLY: TENANT PROVISIONING/ONBOARDING (CREATING A NEW TENANT) IS OUT OF SCOPE.</b> This
/// record — and <see cref="ITenantCatalog"/> as a whole — ships lookup only; a consuming service's
/// own tenant-management surface owns writes (creating, renaming, suspending, offboarding a
/// tenant). Nothing in <c>SharedKernel.MultiTenancy</c> mutates tenant state.
/// </para>
/// <para>
/// <b>Reconciliation, not a fifth unrelated contract.</b> This platform already has three other
/// tenant-adjacent seams, all confirmed unchanged by <see cref="TenantDescriptor"/>'s existence —
/// none of the three needs the full descriptor, all three operate on a bare <see cref="Guid"/>
/// tenant id: <c>ITenantProvider</c> (<c>12.Security.Abstractions</c>, resolves the current
/// request's tenant <i>identity</i>), <c>ICurrentTenantService</c> (a <c>06.Persistence</c>-local
/// seam), and <c>ITenantCacheService</c> (<c>02.Caching.Abstractions</c>).
/// </para>
/// </remarks>
/// <param name="TenantId">The tenant's unique identifier.</param>
/// <param name="DisplayName">A human-readable name for the tenant (e.g. for admin UIs, logs).</param>
/// <param name="Status">The tenant's current lifecycle status.</param>
/// <param name="IsolationMode">How the tenant's data is physically isolated from other tenants.</param>
/// <param name="DefaultCulture">
/// A bare BCL culture name (e.g. <c>"en-US"</c>), or <see langword="null"/> when the tenant has no
/// configured default culture. Forward-compatible for <c>13.ServiceDefaults</c>'s
/// <c>AddSharedKernelLocalization</c> — no localization logic exists anywhere in
/// this type; it is a plain data carrier.
/// </param>
/// <param name="Settings">
/// An extensible, tenant-scoped feature/config bag. Never <see langword="null"/> — an empty
/// dictionary when the tenant has no custom settings.
/// </param>
public sealed record TenantDescriptor(
    Guid TenantId,
    string DisplayName,
    TenantStatus Status,
    TenantIsolationMode IsolationMode,
    string? DefaultCulture,
    IReadOnlyDictionary<string, string> Settings);
