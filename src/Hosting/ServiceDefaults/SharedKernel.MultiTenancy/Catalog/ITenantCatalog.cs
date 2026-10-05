using SharedKernel.Execution.Tenancy;

namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// Read-only lookup of tenant metadata, by tenant id or by a resolution-strategy-supplied raw
/// value (host, claim value, header value).
/// </summary>
/// <remarks>
/// <para>
/// <b>READ-ONLY, IN CAPITALS: TENANT PROVISIONING/ONBOARDING (CREATING A NEW TENANT) IS OUT OF
/// SCOPE — A CONSUMING SERVICE'S OWN TENANT-MANAGEMENT SURFACE OWNS WRITES; THIS SHIPS LOOKUP
/// ONLY.</b> No method on this interface — nor any implementation shipped in this package —
/// creates, renames, or otherwise mutates a tenant.
/// </para>
/// <para>
/// No default implementation is registered by any DI extension method in this package —
/// <see cref="DatabaseTenantCatalog"/> and <see cref="CachedTenantCatalog"/> are opt-in, composed
/// explicitly by the consuming service at its own composition root (see this package's
/// <c>README.md</c> for a worked example), mirroring the bridge-seam pattern already established
/// by <c>Resolution.ITenantStatusValidator</c>.
/// </para>
/// </remarks>
public interface ITenantCatalog
{
    /// <summary>
    /// Looks up a tenant by its unique identifier.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The matching <see cref="TenantDescriptor"/>, or <see langword="null"/> when no tenant with
    /// this id exists. Never throws for an absent tenant.
    /// </returns>
    Task<TenantDescriptor?> GetByIdAsync(TenantId tenantId, CancellationToken ct);

    /// <summary>
    /// Looks up a tenant by a resolution-strategy-supplied raw value — a request host/subdomain,
    /// a JWT claim value, or an HTTP header value — mirroring the existing
    /// <c>TenantResolutionOptions.StrategyOrder</c> resolution shapes.
    /// </summary>
    /// <remarks>
    /// This contract does not re-derive <paramref name="resolutionKey"/> from the request — the
    /// caller supplies whatever raw value an <c>ITenantResolutionStrategy</c> (or equivalent)
    /// already extracted.
    /// </remarks>
    /// <param name="resolutionKey">The raw resolution value (e.g. a request host).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The matching <see cref="TenantDescriptor"/>, or <see langword="null"/> when no tenant
    /// matches this key. Never throws for an absent tenant.
    /// </returns>
    Task<TenantDescriptor?> GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct);
}
